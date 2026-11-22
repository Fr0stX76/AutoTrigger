using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AutoTrigger.Core.Logging;

namespace AutoTrigger.Core.Model
{
    // An .ico made of PNG images, which Windows reads at every size since Vista.
    public static class IconFile
    {
        public const int MaxSide = 256;
        private const int HeaderSize = 6;
        private const int EntrySize = 16;

        // Square images, one per size; the largest comes first, as Windows lists them.
        public static byte[] FromPngs(IEnumerable<KeyValuePair<int, byte[]>> images)
        {
            List<KeyValuePair<int, byte[]>> list = (images ?? Enumerable.Empty<KeyValuePair<int, byte[]>>())
                .Where(i => i.Key >= 1 && i.Key <= MaxSide && i.Value != null && i.Value.Length > 0)
                .OrderByDescending(i => i.Key)
                .ToList();
            if (list.Count == 0)
                throw new ArgumentException("no image for the icon", nameof(images));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)list.Count);
                int offset = HeaderSize + EntrySize * list.Count;
                foreach (KeyValuePair<int, byte[]> image in list)
                {
                    // 0 stands for 256 in the one-byte width and height.
                    byte side = (byte)(image.Key == MaxSide ? 0 : image.Key);
                    writer.Write(side);
                    writer.Write(side);
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write(image.Value.Length);
                    writer.Write(offset);
                    offset += image.Value.Length;
                }
                foreach (KeyValuePair<int, byte[]> image in list)
                    writer.Write(image.Value);
                writer.Flush();
                return stream.ToArray();
            }
        }

        // The sizes an .ico holds, or none when its header or entries do not fit in the file.
        public static IReadOnlyList<int> Sizes(byte[] ico)
        {
            var sizes = new List<int>();
            if (ico == null || ico.Length < HeaderSize || BitConverter.ToUInt16(ico, 0) != 0 || BitConverter.ToUInt16(ico, 2) != 1)
                return sizes;
            int count = BitConverter.ToUInt16(ico, 4);
            if (count == 0 || ico.Length < HeaderSize + EntrySize * count)
                return new List<int>();
            for (int i = 0; i < count; i++)
            {
                int entry = HeaderSize + EntrySize * i;
                long length = BitConverter.ToUInt32(ico, entry + 8);
                long offset = BitConverter.ToUInt32(ico, entry + 12);
                if (length == 0 || offset < HeaderSize + EntrySize * count || offset + length > ico.Length)
                    return new List<int>();
                sizes.Add(ico[entry] == 0 ? MaxSide : ico[entry]);
            }
            return sizes;
        }
    }

    // The icons imported for applications: one .ico per application, named after its id and its content, so a chosen
    // file name never reaches the disk. A new icon gets a new name: Windows keeps shortcut icons by path, and would go on
    // showing the old one. Kept apart from the icon cache, whose clean-up in earlier versions would remove them.
    public sealed class CustomIconStore
    {
        public const string FolderName = "custom-icons";

        // Only files named like the store's own are ever deleted.
        private static readonly Regex OwnFileName = new Regex("^([0-9a-f]{32})-[0-9a-f]{8}\\.ico(\\.tmp)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly string _directory;
        private readonly HashSet<Guid> _missingLogged = new HashSet<Guid>();

        public CustomIconStore(string directory)
        {
            _directory = directory;
        }

        public string PathOf(Guid appId, byte[] ico) => Path.Combine(_directory, FileName(appId, ico));

        // The imported icon to show, or null for the program's own. A missing file is said once in the log.
        public string Existing(AppEntry app)
        {
            if (app == null || !app.CustomIcon)
                return null;
            string file = FilesOf(app.Id).FirstOrDefault();
            if (file != null)
                return file;
            if (_missingLogged.Add(app.Id))
                Log.Warning($"{app.Name}: its imported icon is missing; the program's own icon is used");
            return null;
        }

        // Written whole or not at all: an icon half written would show as blank on the desktop.
        public void Save(Guid appId, byte[] ico)
        {
            if (IconFile.Sizes(ico).Count == 0)
                throw new ArgumentException("not an icon", nameof(ico));
            Directory.CreateDirectory(_directory);
            string file = PathOf(appId, ico);
            string temp = file + ".tmp";
            File.WriteAllBytes(temp, ico);
            if (File.Exists(file))
                File.Replace(temp, file, null);
            else
                File.Move(temp, file);
            _missingLogged.Remove(appId);
            // The new icon is saved by now: an older one that Windows still holds open is left to the next start.
            foreach (string older in FilesOf(appId).Where(f => !string.Equals(f, file, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    File.Delete(older);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Log.Detailed($"Older imported icon not removed yet: {ex.Message}");
                }
            }
        }

        public void Delete(Guid appId)
        {
            foreach (string file in FilesOf(appId))
                File.Delete(file);
        }

        // At start, before anything is imported: icons of applications no longer listed go, and of the others, all but
        // the newest, which is the one shown.
        public int RemoveUnused(IEnumerable<Guid> kept)
        {
            if (!Directory.Exists(_directory))
                return 0;
            var shown = new HashSet<string>(kept.Distinct().SelectMany(id => FilesOf(id).Take(1)), StringComparer.OrdinalIgnoreCase);
            int removed = 0;
            foreach (string file in Directory.GetFiles(_directory))
            {
                if (shown.Contains(file) || !OwnFileName.IsMatch(Path.GetFileName(file)))
                    continue;
                File.Delete(file);
                removed++;
            }
            return removed;
        }

        private IEnumerable<string> FilesOf(Guid appId)
        {
            try
            {
                if (!Directory.Exists(_directory))
                    return Enumerable.Empty<string>();
                string id = appId.ToString("N");
                return Directory.GetFiles(_directory, id + "-*.ico")
                    .Where(f => OwnFileName.IsMatch(Path.GetFileName(f)) && !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Detailed("Imported icons cannot be listed: " + ex.Message);
                return Enumerable.Empty<string>();
            }
        }

        private static string FileName(Guid appId, byte[] ico)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(ico);
                return appId.ToString("N") + "-" + string.Concat(hash.Take(4).Select(b => b.ToString("x2", CultureInfo.InvariantCulture))) + ".ico";
            }
        }
    }
}
