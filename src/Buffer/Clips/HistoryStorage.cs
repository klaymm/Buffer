using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Buffer
{
    // История на диске: список в index.dat и по файлу на элемент. Всё зашифровано DPAPI,
    // прочитать файлы может только эта же учётная запись Windows на этом компьютере.
    sealed class HistoryStorage
    {
        const int FormatVersion = 1;
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Buffer.History.v1");

        readonly string _folder = Path.Combine(AppPaths.DataFolder, "History");
        readonly object _lock = new object();
        Task _queue = Task.CompletedTask;

        string IndexPath => Path.Combine(_folder, "index.dat");
        string ItemsFolder => Path.Combine(_folder, "Items");

        public List<ClipItem> Load(ThumbnailSpec spec)
        {
            var items = new List<ClipItem>();
            try
            {
                if (!File.Exists(IndexPath))
                    return items;
                using (var reader = new BinaryReader(new MemoryStream(Unprotect(File.ReadAllBytes(IndexPath)))))
                {
                    if (reader.ReadInt32() != FormatVersion)
                        return items;
                    int count = reader.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        var id = new Guid(reader.ReadBytes(16));
                        bool pinned = reader.ReadBoolean();
                        var item = LoadItem(id, spec);
                        if (item == null)
                            continue;
                        item.IsPinned = pinned;
                        items.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
            }
            return items;
        }

        // Записывает переданный список целиком: новые элементы добавляются, лишние файлы удаляются.
        public void Save(IList<ClipItem> items)
        {
            var snapshot = new List<(ClipItem Item, bool Pinned)>();
            foreach (var item in items)
                snapshot.Add((item, item.IsPinned));
            lock (_lock)
                _queue = _queue.ContinueWith(_ => Write(snapshot), TaskScheduler.Default);
        }

        public void Flush(TimeSpan timeout)
        {
            Task queue;
            lock (_lock)
                queue = _queue;
            queue.Wait(timeout);
        }

        void Write(List<(ClipItem Item, bool Pinned)> snapshot)
        {
            try
            {
                if (snapshot.Count == 0)
                {
                    if (Directory.Exists(_folder))
                        Directory.Delete(_folder, true);
                    return;
                }

                Directory.CreateDirectory(ItemsFolder);
                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in snapshot)
                {
                    string path = ItemPath(entry.Item.Id);
                    keep.Add(Path.GetFileName(path));
                    if (!File.Exists(path))
                        WriteProtected(path, SerializeItem(entry.Item));
                }
                WriteProtected(IndexPath, SerializeIndex(snapshot));

                foreach (string file in Directory.GetFiles(ItemsFolder))
                {
                    if (!keep.Contains(Path.GetFileName(file)))
                        File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
            }
        }

        string ItemPath(Guid id) => Path.Combine(ItemsFolder, id.ToString("N") + ".dat");

        static byte[] SerializeIndex(List<(ClipItem Item, bool Pinned)> snapshot)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(FormatVersion);
                writer.Write(snapshot.Count);
                foreach (var entry in snapshot)
                {
                    writer.Write(entry.Item.Id.ToByteArray());
                    writer.Write(entry.Pinned);
                }
                writer.Flush();
                return stream.ToArray();
            }
        }

        static byte[] SerializeItem(ClipItem item)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(FormatVersion);
                writer.Write((byte)item.Kind);
                writer.Write(item.IsPrivate);
                writer.Write(item.Created.Ticks);
                writer.Write(item.Hash);
                WriteString(writer, item.Text);
                WriteBytes(writer, item.Html);
                WriteBytes(writer, item.Rtf);
                WriteBytes(writer, item.Png);
                writer.Write(item.PixelWidth);
                writer.Write(item.PixelHeight);
                writer.Write(item.Files?.Length ?? -1);
                if (item.Files != null)
                {
                    foreach (string file in item.Files)
                        writer.Write(file);
                }
                writer.Flush();
                return stream.ToArray();
            }
        }

        ClipItem LoadItem(Guid id, ThumbnailSpec spec)
        {
            try
            {
                string path = ItemPath(id);
                if (!File.Exists(path))
                    return null;
                using (var reader = new BinaryReader(new MemoryStream(Unprotect(File.ReadAllBytes(path))), Encoding.UTF8))
                {
                    if (reader.ReadInt32() != FormatVersion)
                        return null;
                    var item = new ClipItem
                    {
                        Id = id,
                        Kind = (ClipKind)reader.ReadByte(),
                        IsPrivate = reader.ReadBoolean(),
                        Created = new DateTime(reader.ReadInt64(), DateTimeKind.Utc),
                        Hash = reader.ReadUInt64(),
                        Text = ReadString(reader),
                        Html = ReadBytes(reader),
                        Rtf = ReadBytes(reader),
                        Png = ReadBytes(reader),
                        PixelWidth = reader.ReadInt32(),
                        PixelHeight = reader.ReadInt32(),
                    };
                    int files = reader.ReadInt32();
                    if (files >= 0)
                    {
                        item.Files = new string[files];
                        for (int i = 0; i < files; i++)
                            item.Files[i] = reader.ReadString();
                    }

                    if (item.Kind == ClipKind.Image)
                    {
                        var image = item.Png != null ? ImageCodec.DecodePng(item.Png) : null;
                        if (image == null)
                            return null;
                        item.Thumbnail = ImageCodec.MakeThumbnail(image.Bitmap, spec);
                    }
                    item.PrepareDisplay();
                    return item;
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
                return null;
            }
        }

        static void WriteString(BinaryWriter writer, string value)
        {
            writer.Write(value != null);
            if (value != null)
                writer.Write(value);
        }

        static string ReadString(BinaryReader reader) => reader.ReadBoolean() ? reader.ReadString() : null;

        static void WriteBytes(BinaryWriter writer, byte[] value)
        {
            writer.Write(value?.Length ?? -1);
            if (value != null)
                writer.Write(value);
        }

        static byte[] ReadBytes(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            return length < 0 ? null : reader.ReadBytes(length);
        }

        static void WriteProtected(string path, byte[] data)
        {
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser));
            if (File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path);
        }

        static byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
    }
}
