using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;

namespace MuseDesktop
{
    public sealed class Account
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string LastPage { get; set; }
    }

    public sealed class AccountStore
    {
        public readonly string Root;
        public readonly List<Account> Accounts = new List<Account>();
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        string Index { get { return Path.Combine(Root, "accounts.json"); } }

        public AccountStore(string root)
        {
            Root = Path.GetFullPath(root);
            if (!Directory.Exists(Root))
            {
                Directory.CreateDirectory(Root);
                var acl = new DirectorySecurity();
                acl.SetAccessRuleProtection(true, false);
                acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,
                    FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
                acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(Root).SetAccessControl(acl);
            }
            RejectLink(Root);
            if (File.Exists(Index))
            {
                RejectLink(Index);
                if (new FileInfo(Index).Length > 1024 * 1024) throw new InvalidDataException("ACCOUNT_INDEX_SIZE");
                var saved = json.Deserialize<List<Account>>(File.ReadAllText(Index, Encoding.UTF8));
                if (saved == null || saved.Count > 100) throw new InvalidDataException("ACCOUNT_INDEX_INVALID");
                foreach (var account in saved)
                {
                    if (account == null || !ValidId(account.Id) || Accounts.Any(a => a.Id == account.Id))
                        throw new InvalidDataException("ACCOUNT_ID_INVALID");
                    account.Name = ValidateName(account.Name);
                    account.LastPage = SafePage(account.LastPage);
                    Accounts.Add(account);
                }
            }
        }

        public static bool ValidId(string id)
        {
            Guid parsed;
            return id != null && Guid.TryParseExact(id, "N", out parsed);
        }

        public static string ValidateName(string value)
        {
            value = (value ?? "").Trim();
            if (value.Length == 0 || value.Length > 32 || value.Any(char.IsControl))
                throw new ArgumentException("ACCOUNT_NAME_INVALID");
            return value;
        }

        public static string SafePage(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != "https" ||
                !String.Equals(uri.Host, "muse.ai", StringComparison.OrdinalIgnoreCase) ||
                !String.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort) return "https://muse.ai/";
            // Persist conversation paths only. OAuth query/fragment data is never stored.
            if (uri.AbsolutePath == "/" || uri.AbsolutePath.StartsWith("/thread/", StringComparison.Ordinal))
                return uri.GetLeftPart(UriPartial.Path);
            return "https://muse.ai/";
        }

        public Account Add(string name)
        {
            if (Accounts.Count >= 100) throw new InvalidOperationException("ACCOUNT_LIMIT");
            var account = new Account { Id = Guid.NewGuid().ToString("N"), Name = ValidateName(name), LastPage = "https://muse.ai/" };
            Accounts.Add(account);
            try { Save(); } catch { Accounts.Remove(account); throw; }
            return account;
        }

        public void Rename(Account account, string name)
        {
            var previous = account.Name;
            account.Name = ValidateName(name);
            try { Save(); } catch { account.Name = previous; throw; }
        }

        public void Save()
        {
            RejectLink(Root);
            if (File.Exists(Index)) RejectLink(Index);
            string temporary = Path.Combine(Root, "accounts-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(json.Serialize(Accounts));
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(Index)) File.Replace(temporary, Index, null); else File.Move(temporary, Index);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public string Profile(Account account)
        {
            if (!Accounts.Contains(account) || !ValidId(account.Id)) throw new InvalidDataException("PROFILE_ID_INVALID");
            string parent = Path.Combine(Root, "profiles");
            Directory.CreateDirectory(parent);
            RejectLink(Root);
            RejectLink(parent);
            string profile = Path.GetFullPath(Path.Combine(parent, account.Id));
            if (!profile.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("PROFILE_PATH_INVALID");
            if (Directory.Exists(profile)) RejectLink(profile);
            return profile;
        }

        public void Remove(Account account)
        {
            string profile = Profile(account);
            if (Directory.Exists(profile))
            {
                // Refuse traversal through junctions rather than recursively following them.
                VerifyTree(profile);
                Directory.Delete(profile, true);
            }
            int index = Accounts.IndexOf(account);
            Accounts.Remove(account);
            try { Save(); } catch { Accounts.Insert(index, account); throw; }
        }

        static void VerifyTree(string directory)
        {
            RejectLink(directory);
            foreach (string item in Directory.GetFileSystemEntries(directory))
            {
                RejectLink(item);
                if (Directory.Exists(item)) VerifyTree(item);
            }
        }

        static void RejectLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("PROFILE_REPARSE_POINT");
        }
    }
}
