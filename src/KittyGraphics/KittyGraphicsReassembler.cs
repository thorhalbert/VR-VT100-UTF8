using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace libVT100.KittyGraphics
{
    /// <summary>
    /// Defragments multi-chunk Kitty Graphics Protocol payload streams, decompresses zlib payloads,
    /// and decodes pixel data (PNG, RGBA32, RGB24) into <see cref="KittyImage"/> instances.
    /// </summary>
    public sealed class KittyGraphicsReassembler
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, InFlightGfxAssembly> _assemblies = new(StringComparer.OrdinalIgnoreCase);

        public bool ProcessChunk(KittyGraphicsCommand cmd, string payloadChunk, out KittyGraphicsCommand? completeCommand, out string? error)
        {
            completeCommand = null;
            error = null;

            lock (_lock)
            {
                string key = cmd.ImageId != 0 ? cmd.ImageId.ToString() : "_default_gfx_stream_";

                if (cmd.IsMore)
                {
                    if (!_assemblies.TryGetValue(key, out var assembly))
                    {
                        assembly = new InFlightGfxAssembly(key, cmd);
                        _assemblies[key] = assembly;
                    }
                    assembly.Append(payloadChunk);
                    return true;
                }

                string fullBase64;
                KittyGraphicsCommand effectiveCmd = cmd;

                if (_assemblies.TryGetValue(key, out var existingAssembly))
                {
                    existingAssembly.Append(payloadChunk);
                    fullBase64 = existingAssembly.GetCombinedString();
                    effectiveCmd = existingAssembly.InitialCommand;
                    if (cmd.Action != '\0') effectiveCmd.Action = cmd.Action;
                    if (cmd.ColSpan.HasValue) effectiveCmd.ColSpan = cmd.ColSpan;
                    if (cmd.RowSpan.HasValue) effectiveCmd.RowSpan = cmd.RowSpan;
                    if (cmd.ZIndex != 0) effectiveCmd.ZIndex = cmd.ZIndex;
                    _assemblies.Remove(key);
                }
                else
                {
                    fullBase64 = payloadChunk;
                }

                if (effectiveCmd.Action == 'q' || effectiveCmd.Action == 'd' || effectiveCmd.Action == 'p')
                {
                    completeCommand = effectiveCmd;
                    return true;
                }

                try
                {
                    byte[] payloadBytes = string.IsNullOrEmpty(fullBase64) ? Array.Empty<byte>() : Convert.FromBase64String(fullBase64);

                    if (effectiveCmd.Compression == 'z' && payloadBytes.Length > 0)
                    {
                        using var inMs = new MemoryStream(payloadBytes);
                        using var zlib = new ZLibStream(inMs, CompressionMode.Decompress);
                        using var outMs = new MemoryStream();
                        zlib.CopyTo(outMs);
                        payloadBytes = outMs.ToArray();
                    }

                    KittyImage? image = null;

                    if (effectiveCmd.Format == 100) // PNG
                    {
                        if (PngDecoder.TryDecode(payloadBytes, out int w, out int h, out byte[] rgba, out string? pngErr))
                        {
                            image = new KittyImage(effectiveCmd.ImageId, w, h, rgba);
                            effectiveCmd.PixelWidth = w;
                            effectiveCmd.PixelHeight = h;
                        }
                        else
                        {
                            error = pngErr ?? "PNG decoding failed.";
                            return false;
                        }
                    }
                    else if (effectiveCmd.Format == 24) // RGB
                    {
                        if (effectiveCmd.PixelWidth <= 0 || effectiveCmd.PixelHeight <= 0)
                        {
                            error = "Missing dimensions for raw RGB.";
                            return false;
                        }
                        image = KittyImage.FromRgb(effectiveCmd.ImageId, effectiveCmd.PixelWidth, effectiveCmd.PixelHeight, payloadBytes);
                    }
                    else // RGBA
                    {
                        if (effectiveCmd.PixelWidth <= 0 || effectiveCmd.PixelHeight <= 0)
                        {
                            error = "Missing dimensions for raw RGBA.";
                            return false;
                        }
                        image = new KittyImage(effectiveCmd.ImageId, effectiveCmd.PixelWidth, effectiveCmd.PixelHeight, payloadBytes);
                    }

                    effectiveCmd.Image = image;
                    completeCommand = effectiveCmd;
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"Decode error: {ex.Message}";
                    return false;
                }
            }
        }

        private sealed class InFlightGfxAssembly
        {
            public string Key { get; }
            public KittyGraphicsCommand InitialCommand { get; }
            private readonly StringBuilder _sb = new();
            public InFlightGfxAssembly(string key, KittyGraphicsCommand initialCommand)
            {
                Key = key;
                InitialCommand = initialCommand;
            }
            public void Append(string chunk) => _sb.Append(chunk);
            public string GetCombinedString() => _sb.ToString();
        }
    }
}
