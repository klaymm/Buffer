using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Buffer
{
    sealed class DecodedImage
    {
        public BitmapSource Bitmap;
        public byte[] Pixels;
        public int Width;
        public int Height;
    }

    static class ImageCodec
    {
        const int BI_RGB = 0;
        const int BI_BITFIELDS = 3;

        public static DecodedImage DecodePng(byte[] data)
        {
            try
            {
                using (var stream = new MemoryStream(data))
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                    return FromBitmap(decoder.Frames[0]);
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
                return null;
            }
        }

        public static DecodedImage DecodeDib(byte[] dib)
        {
            try
            {
                return ParseDib(dib) ?? DecodeBmpFile(dib);
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
                return null;
            }
        }

        static DecodedImage FromBitmap(BitmapSource source)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = converted.PixelWidth, height = converted.PixelHeight, stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);
            return Create(pixels, width, height);
        }

        static DecodedImage Create(byte[] pixels, int width, int height)
        {
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            bitmap.Freeze();
            return new DecodedImage { Bitmap = bitmap, Pixels = pixels, Width = width, Height = height };
        }

        // 24 и 32 бита разбираем сами: декодер BMP в Windows не всегда правильно понимает альфа-канал.
        static DecodedImage ParseDib(byte[] dib)
        {
            if (dib == null || dib.Length < 40)
                return null;
            int headerSize = BitConverter.ToInt32(dib, 0);
            int width = BitConverter.ToInt32(dib, 4);
            int height = BitConverter.ToInt32(dib, 8);
            int bitCount = BitConverter.ToInt16(dib, 14);
            int compression = BitConverter.ToInt32(dib, 16);
            int colorsUsed = BitConverter.ToInt32(dib, 32);
            if (headerSize < 40 || width <= 0 || height == 0)
                return null;
            if ((bitCount != 32 && bitCount != 24) || (compression != BI_RGB && compression != BI_BITFIELDS))
                return null;

            bool topDown = height < 0;
            height = Math.Abs(height);
            int offset = headerSize + (compression == BI_BITFIELDS && headerSize == 40 ? 12 : 0) + colorsUsed * 4;
            int sourceStride = (width * bitCount + 31) / 32 * 4;
            if (offset + (long)sourceStride * height > dib.Length)
                return null;

            int stride = width * 4;
            var pixels = new byte[(long)stride * height];
            bool hasAlpha = false;
            for (int y = 0; y < height; y++)
            {
                int source = offset + (topDown ? y : height - 1 - y) * sourceStride;
                int target = y * stride;
                if (bitCount == 32)
                {
                    System.Buffer.BlockCopy(dib, source, pixels, target, stride);
                    if (!hasAlpha)
                    {
                        for (int x = 3; x < stride; x += 4)
                        {
                            if (pixels[target + x] != 0)
                            {
                                hasAlpha = true;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    for (int x = 0; x < width; x++, source += 3, target += 4)
                    {
                        pixels[target] = dib[source];
                        pixels[target + 1] = dib[source + 1];
                        pixels[target + 2] = dib[source + 2];
                        pixels[target + 3] = 255;
                    }
                }
            }

            // Нулевой альфа-канал у 32-битного DIB означает «не используется», а не «прозрачно».
            if (bitCount == 32 && !hasAlpha)
            {
                for (int i = 3; i < pixels.Length; i += 4)
                    pixels[i] = 255;
            }
            return Create(pixels, width, height);
        }

        static DecodedImage DecodeBmpFile(byte[] dib)
        {
            if (dib == null || dib.Length < 40)
                return null;
            int headerSize = BitConverter.ToInt32(dib, 0);
            int bitCount = BitConverter.ToInt16(dib, 14);
            int compression = BitConverter.ToInt32(dib, 16);
            int colorsUsed = BitConverter.ToInt32(dib, 32);
            int palette = colorsUsed > 0 ? colorsUsed : (bitCount <= 8 ? 1 << bitCount : 0);
            int bitsOffset = 14 + headerSize + (compression == BI_BITFIELDS && headerSize == 40 ? 12 : 0) + palette * 4;

            var file = new byte[14 + dib.Length];
            file[0] = (byte)'B';
            file[1] = (byte)'M';
            BitConverter.GetBytes(file.Length).CopyTo(file, 2);
            BitConverter.GetBytes(bitsOffset).CopyTo(file, 10);
            System.Buffer.BlockCopy(dib, 0, file, 14, dib.Length);
            using (var stream = new MemoryStream(file))
            {
                var decoder = new BmpBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                return FromBitmap(decoder.Frames[0]);
            }
        }

        public static byte[] EncodePng(BitmapSource bitmap)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        // CF_DIB для программ, которые не понимают PNG. Полупрозрачное смешивается с белым:
        // такие программы обычно игнорируют альфа-канал и показали бы чёрный фон.
        public static byte[] ToDib(DecodedImage image)
        {
            int width = image.Width, height = image.Height, stride = width * 4;
            var dib = new byte[40 + (long)stride * height];
            BitConverter.GetBytes(40).CopyTo(dib, 0);
            BitConverter.GetBytes(width).CopyTo(dib, 4);
            BitConverter.GetBytes(height).CopyTo(dib, 8);
            BitConverter.GetBytes((short)1).CopyTo(dib, 12);
            BitConverter.GetBytes((short)32).CopyTo(dib, 14);
            BitConverter.GetBytes(BI_RGB).CopyTo(dib, 16);
            BitConverter.GetBytes(stride * height).CopyTo(dib, 20);

            byte[] pixels = image.Pixels;
            for (int y = 0; y < height; y++)
            {
                int source = y * stride;
                int target = 40 + (height - 1 - y) * stride;
                for (int x = 0; x < stride; x += 4)
                {
                    int alpha = pixels[source + x + 3];
                    dib[target + x] = Blend(pixels[source + x], alpha);
                    dib[target + x + 1] = Blend(pixels[source + x + 1], alpha);
                    dib[target + x + 2] = Blend(pixels[source + x + 2], alpha);
                    dib[target + x + 3] = 255;
                }
            }
            return dib;
        }

        static byte Blend(byte value, int alpha) => (byte)((value * alpha + 255 * (255 - alpha)) / 255);

        public static BitmapSource MakeThumbnail(BitmapSource source, ThumbnailSpec spec)
        {
            double scale = Math.Min(1.0, Math.Min(spec.MaxWidth / (double)source.PixelWidth, spec.MaxHeight / (double)source.PixelHeight));
            BitmapSource scaled = scale < 1.0 ? new TransformedBitmap(source, new ScaleTransform(scale, scale)) : source;
            var converted = new FormatConvertedBitmap(scaled, PixelFormats.Pbgra32, null, 0);
            int width = converted.PixelWidth, height = converted.PixelHeight, stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);
            var thumbnail = BitmapSource.Create(width, height, spec.Dpi, spec.Dpi, PixelFormats.Pbgra32, null, pixels, stride);
            thumbnail.Freeze();
            return thumbnail;
        }
    }
}
