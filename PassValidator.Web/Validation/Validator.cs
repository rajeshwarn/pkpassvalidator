﻿using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography.Pkcs;
using System.Text;

namespace PassValidator.Web.Validation
{
    public class Validator
    {
        public ValidationResult Validate(byte[] passContent)
        {
            ValidationResult result = new ValidationResult();

            string passTypeIdentifier = null;
            string teamIdentifier = null;
            string signaturePassTypeIdentifier = null;
            string signatureTeamIdentifier = null;

            using (MemoryStream zipToOpen = new MemoryStream(passContent))
            {
                using (ZipArchive archive = new ZipArchive(zipToOpen, ZipArchiveMode.Read, false))
                {
                    byte[] manifestFile = null;

                    foreach (var e in archive.Entries)
                    {
                        if (e.Name.ToLower().Equals("manifest.json"))
                        {
                            result.HasManifest = true;

                            using (var stream = e.Open())
                            {
                                using (var ms = new MemoryStream())
                                {
                                    stream.CopyTo(ms);
                                    ms.Position = 0;
                                    manifestFile = ms.ToArray();
                                }
                            }
                        }

                        if (e.Name.ToLower().Equals("pass.json"))
                        {
                            result.HasPass = true;

                            using (var stream = e.Open())
                            {
                                using (var ms = new MemoryStream())
                                {
                                    stream.CopyTo(ms);
                                    ms.Position = 0;
                                    var file = ms.ToArray();

                                    var jsonObject = JObject.Parse(Encoding.UTF8.GetString(file));
                                    passTypeIdentifier = jsonObject["passTypeIdentifier"].Value<string>();
                                    teamIdentifier = jsonObject["teamIdentifier"].Value<string>();
                                }
                            }
                        }

                        if (e.Name.ToLower().Equals("signature"))
                        {
                            ContentInfo contentInfo = new ContentInfo(manifestFile);
                            SignedCms signedCms = new SignedCms(contentInfo, true);

                            try
                            {
                                signedCms.CheckSignature(true);
                            }
                            catch
                            {

                            }

                            using (var stream = e.Open())
                            {
                                using (var ms = new MemoryStream())
                                {
                                    stream.CopyTo(ms);
                                    ms.Position = 0;

                                    signedCms.Decode(ms.ToArray());

                                    var signer = signedCms.SignerInfos[0];

                                    result.SignedByApple = signer.Certificate.IssuerName.Name == "CN=Apple Worldwide Developer Relations Certification Authority, OU=Apple Worldwide Developer Relations, O=Apple Inc., C=US";

                                    if (result.SignedByApple)
                                    {
                                        Debug.WriteLine(signer.Certificate);

                                        var cnValues = Parse(signer.Certificate.Subject, "CN");
                                        var ouValues = Parse(signer.Certificate.Subject, "OU");

                                        var passTypeIdentifierSubject = cnValues[0];
                                        signaturePassTypeIdentifier = passTypeIdentifierSubject.Replace("Pass Type ID: ", "");

                                        if (ouValues != null && ouValues.Count > 0)
                                        {
                                            signatureTeamIdentifier = ouValues[0];
                                        }

                                        Debug.WriteLine(signer.Certificate.IssuerName.Name);
                                    }
                                }
                            }

                            result.HasSignature = true;
                        }

                        Debug.WriteLine(e.FullName);
                    }
                }
            }

            result.PassTypeIdentifierMatches = passTypeIdentifier == signaturePassTypeIdentifier;
            result.TeamIdentifierMatches = teamIdentifier == signatureTeamIdentifier;

            return result;
        }

        public static List<string> Parse(string data, string delimiter)
        {
            if (data == null) return null;
            if (!delimiter.EndsWith("=")) delimiter = delimiter + "=";
            if (!data.Contains(delimiter)) return null;
            //base case
            var result = new List<string>();
            int start = data.IndexOf(delimiter) + delimiter.Length;
            int length = data.IndexOf(',', start) - start;
            if (length == 0) return null; //the group is empty
            if (length > 0)
            {
                result.Add(data.Substring(start, length));
                //only need to recurse when the comma was found, because there could be more groups
                var rec = Parse(data.Substring(start + length), delimiter);
                if (rec != null) result.AddRange(rec); //can't pass null into AddRange() :(
            }
            else //no comma found after current group so just use the whole remaining string
            {
                result.Add(data.Substring(start));
            }
            return result;
        }
    }
}
