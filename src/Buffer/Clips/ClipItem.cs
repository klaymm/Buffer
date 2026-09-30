using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;

namespace Buffer
{
    public enum ClipKind
    {
        Text = 1,
        Image = 2,
        Files = 3,
    }

    public sealed class FileEntry
    {
        public string Name { get; set; }
        public bool IsFolder { get; set; }
        public string Glyph => IsFolder ? "" : "";
    }

    public sealed class ClipItem : INotifyPropertyChanged
    {
        const int PreviewLength = 2000;
        const int VisibleFiles = 3;

        bool _isPinned;
        ImageSource _thumbnail;

        public Guid Id { get; set; } = Guid.NewGuid();
        public ClipKind Kind { get; set; }
        public DateTime Created { get; set; } = DateTime.UtcNow;
        public bool IsPrivate { get; set; }
        public ulong Hash { get; set; }

        public string Text { get; set; }
        public byte[] Html { get; set; }
        public byte[] Rtf { get; set; }

        public byte[] Png { get; set; }
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }

        public string[] Files { get; set; }

        public bool IsPinned
        {
            get => _isPinned;
            set
            {
                if (_isPinned == value)
                    return;
                _isPinned = value;
                OnPropertyChanged(nameof(IsPinned));
            }
        }

        public ImageSource Thumbnail
        {
            get => _thumbnail;
            set
            {
                _thumbnail = value;
                OnPropertyChanged(nameof(Thumbnail));
            }
        }

        public string Preview { get; private set; }
        public IReadOnlyList<FileEntry> FileEntries { get; private set; }
        public int HiddenFiles { get; private set; }
        public bool HasHiddenFiles => HiddenFiles > 0;
        public string HiddenFilesText => Loc.Instance.MoreFiles(HiddenFiles);

        public string AccessibleName
        {
            get
            {
                switch (Kind)
                {
                    case ClipKind.Image: return Loc.Instance.ImageDescription(PixelWidth, PixelHeight);
                    case ClipKind.Files: return Loc.Instance.FilesDescription(Files?.Length ?? 0) + ": " + string.Join(", ", Array.ConvertAll(Files ?? new string[0], Path.GetFileName));
                    default: return Preview;
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        // Вызывается в фоновом потоке: проверка папок может обращаться к сетевым дискам.
        public void PrepareDisplay()
        {
            if (Kind == ClipKind.Text && Text != null)
            {
                string preview = Text.Length > PreviewLength ? Text.Substring(0, PreviewLength) : Text;
                Preview = preview.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ").Trim();
            }
            else if (Kind == ClipKind.Files && Files != null)
            {
                var entries = new List<FileEntry>();
                for (int i = 0; i < Files.Length && i < VisibleFiles; i++)
                {
                    string path = Files[i];
                    string name = Path.GetFileName(path.TrimEnd('\\', '/'));
                    entries.Add(new FileEntry { Name = string.IsNullOrEmpty(name) ? path : name, IsFolder = SafeIsFolder(path) });
                }
                FileEntries = entries;
                HiddenFiles = Math.Max(0, Files.Length - VisibleFiles);
            }
        }

        public void RefreshTexts() => OnPropertyChanged(string.Empty);

        static bool SafeIsFolder(string path)
        {
            try
            {
                return Directory.Exists(path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    readonly struct ThumbnailSpec
    {
        public ThumbnailSpec(double scale)
        {
            Dpi = 96 * scale;
            MaxWidth = (int)Math.Round(ContentWidth * scale);
            MaxHeight = (int)Math.Round(ContentHeight * scale);
        }

        // Картинка помещается в карточку обычной высоты, как в Windows: 78 минус рамка и отступы.
        public const int ContentWidth = 275;
        public const int ContentHeight = 68;

        public double Dpi { get; }
        public int MaxWidth { get; }
        public int MaxHeight { get; }
    }

    static class ClipItemFactory
    {
        public static ClipItem Create(CapturedClip clip, ThumbnailSpec spec)
        {
            var item = new ClipItem { Kind = clip.Kind, IsPrivate = clip.IsPrivate };
            switch (clip.Kind)
            {
                case ClipKind.Text:
                    item.Text = clip.Text;
                    item.Html = clip.Html;
                    item.Rtf = clip.Rtf;
                    item.Hash = Fnv.Hash(clip.Text);
                    break;

                case ClipKind.Files:
                    item.Files = clip.Files;
                    item.Hash = Fnv.Hash(string.Join("\n", clip.Files));
                    break;

                case ClipKind.Image:
                    var image = clip.Png != null ? ImageCodec.DecodePng(clip.Png) : ImageCodec.DecodeDib(clip.Dib);
                    if (image == null)
                        return null;
                    item.PixelWidth = image.Width;
                    item.PixelHeight = image.Height;
                    item.Hash = Fnv.Hash(image.Pixels) ^ ((ulong)(uint)image.Width << 32 | (uint)image.Height);
                    item.Png = clip.Png ?? ImageCodec.EncodePng(image.Bitmap);
                    item.Thumbnail = ImageCodec.MakeThumbnail(image.Bitmap, spec);
                    break;
            }
            item.PrepareDisplay();
            return item;
        }
    }

    static class Fnv
    {
        const ulong Offset = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        public static ulong Hash(byte[] data)
        {
            ulong hash = Offset;
            for (int i = 0; i < data.Length; i++)
            {
                hash ^= data[i];
                hash *= Prime;
            }
            return hash;
        }

        public static ulong Hash(string text)
        {
            ulong hash = Offset;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= Prime;
            }
            return hash;
        }
    }
}
