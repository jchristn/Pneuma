namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Security;
    using Touchstone.Core;

    /// <summary>
    /// Unit suite for cryptography and RBAC evaluation.
    /// </summary>
    public static class SecuritySuite
    {
        /// <summary>Build the security suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Security",
                displayName: "Security",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("Security", "Aes_RoundTrip", "AES-256 encrypt/decrypt round-trips",
                        executeAsync: _ =>
                        {
                            Aes256Cipher cipher = new Aes256Cipher("test-signing-key");
                            string plaintext = "the quick brown fox";
                            string encrypted = cipher.Encrypt(plaintext);
                            if (cipher.Decrypt(encrypted) != plaintext) throw new Exception("Round-trip failed");
                            string encrypted2 = cipher.Encrypt(plaintext);
                            if (encrypted == encrypted2) throw new Exception("IV is not random per encryption");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("Security", "Password_Verify", "Password hashing verifies correctly",
                        executeAsync: _ =>
                        {
                            string hash = PasswordHasher.Hash("hunter2");
                            if (!PasswordHasher.Verify("hunter2", hash)) throw new Exception("Correct password failed to verify");
                            if (PasswordHasher.Verify("wrong", hash)) throw new Exception("Wrong password verified");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("Security", "Rbac_WildcardPermit", "Wildcard permit allows any operation",
                        executeAsync: _ =>
                        {
                            List<Permission> perms = new List<Permission>
                            {
                                new Permission
                                {
                                    PermissionType = PermissionTypeEnum.Permit,
                                    ResourceTypes = new List<ResourceTypeEnum> { ResourceTypeEnum.All },
                                    OperationTypes = new List<OperationTypeEnum> { OperationTypeEnum.All }
                                }
                            };
                            if (!PermissionEvaluator.IsPermitted(perms, ResourceTypeEnum.Subject, OperationTypeEnum.Delete))
                                throw new Exception("Wildcard permit did not allow delete");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("Security", "Rbac_DenyWins", "Explicit deny overrides permit",
                        executeAsync: _ =>
                        {
                            List<Permission> perms = new List<Permission>
                            {
                                new Permission
                                {
                                    PermissionType = PermissionTypeEnum.Permit,
                                    ResourceTypes = new List<ResourceTypeEnum> { ResourceTypeEnum.Subject },
                                    OperationTypes = new List<OperationTypeEnum> { OperationTypeEnum.Read }
                                },
                                new Permission
                                {
                                    PermissionType = PermissionTypeEnum.Deny,
                                    ResourceTypes = new List<ResourceTypeEnum> { ResourceTypeEnum.Subject },
                                    OperationTypes = new List<OperationTypeEnum> { OperationTypeEnum.Read }
                                }
                            };
                            if (PermissionEvaluator.Evaluate(perms, ResourceTypeEnum.Subject, OperationTypeEnum.Read) != AuthorizationResultEnum.DeniedExplicit)
                                throw new Exception("Deny did not win");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("Security", "Rbac_WriteExpansion", "Write shorthand covers create/update/delete",
                        executeAsync: _ =>
                        {
                            List<Permission> perms = new List<Permission>
                            {
                                new Permission
                                {
                                    PermissionType = PermissionTypeEnum.Permit,
                                    ResourceTypes = new List<ResourceTypeEnum> { ResourceTypeEnum.Subject },
                                    OperationTypes = new List<OperationTypeEnum> { OperationTypeEnum.Write }
                                }
                            };
                            if (!PermissionEvaluator.IsPermitted(perms, ResourceTypeEnum.Subject, OperationTypeEnum.Create))
                                throw new Exception("Write did not cover Create");
                            if (PermissionEvaluator.IsPermitted(perms, ResourceTypeEnum.Subject, OperationTypeEnum.Read))
                                throw new Exception("Write incorrectly covered Read");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("Security", "Rbac_ImplicitDeny", "No matching permission denies implicitly",
                        executeAsync: _ =>
                        {
                            List<Permission> perms = new List<Permission>();
                            if (PermissionEvaluator.Evaluate(perms, ResourceTypeEnum.Subject, OperationTypeEnum.Read) != AuthorizationResultEnum.DeniedImplicit)
                                throw new Exception("Empty permission set was not an implicit deny");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("Security", "TokenCodec_RoundTrip", "Session token encodes and decodes",
                        executeAsync: _ =>
                        {
                            SessionTokenCodec codec = new SessionTokenCodec("signing-key-123");
                            TokenPayload payload = new TokenPayload
                            {
                                SessionId = "ses_abc",
                                PrincipalType = PrincipalTypeEnum.User,
                                TenantId = "ten_1",
                                UserId = "usr_1",
                                TokenId = "nonce",
                                ExpiresUtc = DateTime.UtcNow.AddHours(1)
                            };
                            string token = codec.Encode(payload);
                            TokenPayload? decoded = codec.Decode(token);
                            if (decoded == null) throw new Exception("decode returned null");
                            if (decoded.SessionId != "ses_abc" || decoded.UserId != "usr_1")
                                throw new Exception("decoded payload did not round-trip");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("Security", "TokenCodec_TamperRejected", "A garbage or wrong-key token decodes to null",
                        executeAsync: _ =>
                        {
                            SessionTokenCodec codec = new SessionTokenCodec("signing-key-123");
                            if (codec.Decode("not-a-real-token") != null) throw new Exception("garbage token should not decode");

                            SessionTokenCodec other = new SessionTokenCodec("signing-key-123");
                            string token = other.Encode(new TokenPayload { SessionId = "s" });
                            SessionTokenCodec wrongKey = new SessionTokenCodec("different-key");
                            if (wrongKey.Decode(token) != null) throw new Exception("token from a different key should not decode");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("Security", "KeyGenerator_Format", "Access and secret keys use the expected prefixes and entropy",
                        executeAsync: _ =>
                        {
                            string access = KeyGenerator.GenerateAccessKey();
                            string secret = KeyGenerator.GenerateSecretKey();
                            if (!access.StartsWith("access_", StringComparison.Ordinal) || access.Length < 39)
                                throw new Exception("access key format wrong: " + access);
                            if (!secret.StartsWith("secret_", StringComparison.Ordinal) || secret.Length < 55)
                                throw new Exception("secret key format wrong");
                            if (KeyGenerator.GenerateAccessKey() == access) throw new Exception("access keys should be unique");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("Security", "Aes_EmptyString_RoundTrips", "AES round-trips an empty string",
                        executeAsync: _ =>
                        {
                            Aes256Cipher cipher = new Aes256Cipher("test-signing-key");
                            if (cipher.Decrypt(cipher.Encrypt(String.Empty)) != String.Empty)
                                throw new Exception("empty-string round-trip failed");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("Security", "Aes_MalformedPayloadRejected", "Decrypting a malformed or truncated payload throws",
                        executeAsync: _ =>
                        {
                            Aes256Cipher cipher = new Aes256Cipher("test-signing-key");

                            bool garbageThrew = false;
                            try { cipher.Decrypt("!!!!"); } catch (Exception) { garbageThrew = true; }
                            if (!garbageThrew) throw new Exception("non-base64 payload should throw");

                            // Valid base64 but far shorter than the 16-byte IV guard.
                            bool shortThrew = false;
                            try { cipher.Decrypt("AAAA"); } catch (FormatException) { shortThrew = true; }
                            if (!shortThrew) throw new Exception("payload shorter than the IV should throw FormatException");

                            // A truncated real ciphertext must not silently decrypt to the original.
                            string valid = cipher.Encrypt("the quick brown fox");
                            bool tamperFailed = false;
                            try
                            {
                                string plain = cipher.Decrypt(valid.Substring(0, valid.Length - 4));
                                if (plain != "the quick brown fox") tamperFailed = true;
                            }
                            catch (Exception) { tamperFailed = true; }
                            if (!tamperFailed) throw new Exception("a truncated ciphertext should not decrypt to the original plaintext");

                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("Security", "Password_DeterministicHexAndRejectsBadHash", "Hash is deterministic 64-char hex; verify rejects empty/short stored hashes",
                        executeAsync: _ =>
                        {
                            string first = PasswordHasher.Hash("correct horse");
                            string second = PasswordHasher.Hash("correct horse");
                            if (first != second) throw new Exception("SHA-256 hash should be deterministic");
                            if (first.Length != 64) throw new Exception("hash should be 64 hex characters, got " + first.Length);
                            foreach (char c in first)
                            {
                                bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                                if (!isHex) throw new Exception("hash should be lowercase hex, saw '" + c + "'");
                            }
                            if (!PasswordHasher.Verify("correct horse", first)) throw new Exception("correct password failed to verify");
                            if (PasswordHasher.Verify("correct horse", String.Empty)) throw new Exception("empty stored hash must not verify");
                            if (PasswordHasher.Verify("correct horse", "deadbeef")) throw new Exception("mismatched short hash must not verify");
                            return Task.CompletedTask;
                        })
                });
        }
    }
}
