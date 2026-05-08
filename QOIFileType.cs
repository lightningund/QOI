// Originally from:
// https://github.com/sbtrn-devil/pdn-json/blob/main/PdnJsonFileType.cs
// Acquired on 06/05/25

// Paint dot net plugin to save and load QOI images

// Run with `dotnet build`. It places the dll directly into the FileTypes folder so the terminal needs to be admin and Paint.NET needs to be closed
// To place it locally just change `<OutputPath>` to be a local folder, say `result`

using PaintDotNet;
using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Runtime.InteropServices;

namespace QOIFileType {
	public sealed class QOIFileTypeFactory : IFileTypeFactory {
		public FileType[] GetFileTypeInstances() {
			return [new QOIFileTypePlugin()];
		}
	}

	[PluginSupportInfo(typeof(PluginSupportInfo))]
	internal class QOIFileTypePlugin : FileType {
		// From https://stackoverflow.com/a/4074557
		internal static T ReadType<T>(BinaryReader reader) {
			byte[] bytes = reader.ReadBytes(Marshal.SizeOf<T>());

			GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
			T theStructure = Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
			handle.Free();

			return theStructure;
		}

		internal readonly struct SmallColor(byte r, byte g, byte b, byte a = 255) {
			public readonly byte R = r;
			public readonly byte G = g;
			public readonly byte B = b;
			public readonly byte A = a;

			public Color ToColor() {
				return Color.FromArgb(A, R, G, B);
			}

			public override string ToString() {
				return R + ", " + G + ", " + B + ", " + A;
			}

			public static SmallColor operator +(SmallColor left, SmallColor right) {
				return new SmallColor(
					(byte)(left.R + right.R),
					(byte)(left.G + right.G),
					(byte)(left.B + right.B),
					(byte)(left.A + right.A)
				);
			}

			public static SmallColor operator -(SmallColor left, SmallColor right) {
				return new SmallColor(
					(byte)(left.R - right.R),
					(byte)(left.G - right.G),
					(byte)(left.B - right.B),
					(byte)(left.A - right.A)
				);
			}
		}

		internal static int PixelHash(SmallColor c) {
			return (c.R * 3 + c.G * 5 + c.B * 7 + c.A * 11) % 64;
		}

		/// <summary>
		/// Constructs a QOIFileTypePlugin instance
		/// </summary>
		internal QOIFileTypePlugin()
			: base(
				"QOI",
				new FileTypeOptions{
					LoadExtensions = [".qoi", ".qoif"],
					SaveExtensions = [".qoi", ".qoif"]
				}
			) {}

		/// <summary>
		/// Saves a document to a stream respecting the properties
		/// </summary>
		protected override void OnSave(
			Document input,
			Stream output,
			SaveConfigToken token,
			Surface scratchSurface,
			ProgressEventHandler progressCallback
		) {
			using BinaryWriter writer = new(output, Encoding.UTF8, true);

			uint width = (uint)input.Width;
			uint height = (uint)input.Height;
			if (BitConverter.IsLittleEndian) {
				width = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(width);
				height = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(height);
			}

			// Write the header
			writer.Write(['q', 'o', 'i', 'f']); // Magic
			writer.Write(width);
			writer.Write(height);
			writer.Write((byte)4); // Channels
			writer.Write((byte)0); // Colorspace
			Surface boring = new(input.Width, input.Height);
			input.Flatten(boring);
			// Currently just gonna write each color as the full description
			for (int j = 0; j < input.Height; ++j) {
				for (int i = 0; i < input.Width; ++i) {
					var col = boring[i, j];
					bool needAlpha = col.A != 255;
					writer.Write((byte)(needAlpha ? 0xFF : 0xFE));
					writer.Write(col.R);
					writer.Write(col.G);
					writer.Write(col.B);
					if (needAlpha) writer.Write(col.A);
				}
			}
		}

		/// <summary>
		/// Creates a document from a stream
		/// </summary>
		protected override Document OnLoad(Stream input) {
			Document doc = null;

			try {
				using var reader = new BinaryReader(input);
				var magic = reader.ReadChars(4);
				// TODO: actually check that it matches

				// Read the header
				uint width = reader.ReadUInt32();
				uint height = reader.ReadUInt32();
				// Read past the channels and colorspace which are unused
				// TODO: Deal with these maybe?
				reader.ReadBytes(2);

				if (BitConverter.IsLittleEndian) {
					width = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(width);
					height = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(height);
				}

				SmallColor prev = new(0, 0, 0, 255);
				SmallColor[] seen = new SmallColor[64];
				int idx = 0;
				int lastIdx = (int)(width * height);

				using Bitmap bmp = new((int)width, (int)height, PixelFormat.Format32bppArgb);

				void SetPixel(SmallColor col) {
					int x = idx % (int)width;
					int y = idx / (int)width;
					bmp.SetPixel(x, y, col.ToColor());
					prev = col;
					seen[PixelHash(col)] = col;
					++idx;
				}

				while (true) {
					if (idx >= lastIdx) break;

					byte read = reader.ReadByte();

					if (read == 0b11111110) { // QOI_OP_RGB
						byte[] colors = reader.ReadBytes(3);
						SmallColor alphed = new(colors[0], colors[1], colors[2], prev.A);
						SetPixel(alphed);
						continue;
					}

					if (read == 0b11111111) { // QOI_OP_RGBA
						byte[] colors = reader.ReadBytes(4);
						SetPixel(new SmallColor(colors[0], colors[1], colors[2], colors[3]));
						continue;
					}

					byte opcode = (byte)((read >>> 6) & 0b11);

					switch (opcode) {
						case 0b00: { // QOI_OP_INDEX
							SetPixel(seen[read & 0b111111]);
							break;
						}
						case 0b01: { // QOI_OP_DIFF
							byte dr = (byte)(((read & 0b110000) >>> 4) - 2);
							byte dg = (byte)(((read & 0b1100) >>> 2) - 2);
							byte db = (byte)((read & 0b11) - 2);
							SmallColor delta = new(dr, dg, db, 0);
							SetPixel(delta + prev);
							break;
						}
						case 0b10: { // QOI_OP_LUMA
							byte second = reader.ReadByte();
							byte dg = (byte)((read & 0b111111) - 32);
							byte dr = (byte)(((second & 0b11110000) >>> 4) - 8 + dg);
							byte db = (byte)((second & 0b1111) - 8 + dg);
							SmallColor delta = new(dr, dg, db, 0);
							SetPixel(delta + prev);
							break;
						}
						case 0b11: { // QOI_OP_RUN
							int len = (read & 0b111111) + 1;
							for (int i = 0; i < len; ++i) SetPixel(prev);
							break;
						}
					}
				}

				// Create a document from the image
				doc = Document.FromImage(bmp);
			} catch (Exception e) {
				doc?.Dispose();
				throw new FormatException("Error loading file - " + e.Message, e);
			}

			return doc;
		}
	}
}
