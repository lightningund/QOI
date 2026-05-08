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

			public static bool operator ==(SmallColor left, SmallColor right) {
				return
					left.R == right.R &&
					left.G == right.G &&
					left.B == right.B &&
					left.A == right.A;
			}

			public static bool operator !=(SmallColor left, SmallColor right) {
				return !(left == right);
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

			SmallColor getCol(int idx) {
				int x = idx % input.Width;
				int y = idx / input.Width;
				var col = boring[x, y];
				return new SmallColor(col.R, col.G, col.B, col.A);
			}

			// Testcard with different saving methods:
			// Supplied: 22kb
			// All RGBA (not saved): 321kb
			// Let alpha default to 255 (v1): 256kb
			// Use OP_DIFF for small differences (v2): 86.6kb
			// Use OP_RUN for same pixels (v4): 37.3kb

			SmallColor prev = new(0, 0, 0, 255);
			int lastIdx = input.Width * input.Height;

			for (int idx = 0; idx < lastIdx; ++idx) {
				var col = getCol(idx);

				if (col == prev) {
					int len = 1;
					// Find out how many in a row are the same
					while ((idx < lastIdx - 1) && (getCol(++idx) == prev) && (len < 62)) { ++len; }
					int op = 0b11000000;
					op |= len - 1;
					writer.Write((byte)op);

					// If we hit the end, get out of here
					if (idx >= lastIdx) return;

					// Otherwise continue on like normal with the next different color
					col = getCol(idx);
				}

				var diff = col - prev;
				var smallDiff = diff + new SmallColor(2, 2, 2, 0);
				if ( // OP_DIFF
					smallDiff.A == 0 &&
					smallDiff.R >= 0 && smallDiff.R < 4 &&
					smallDiff.G >= 0 && smallDiff.G < 4 &&
					smallDiff.B >= 0 && smallDiff.B < 4
				) {
					int op = 0b01000000;
					op |= smallDiff.R << 4;
					op |= smallDiff.G << 2;
					op |= smallDiff.B;
					writer.Write((byte)op);
				} else { // Full specification (OP_RGB/OP_RGBA)
					bool needAlpha = col.A != 255;
					writer.Write((byte)(needAlpha ? 0xFF : 0xFE));
					writer.Write(col.R);
					writer.Write(col.G);
					writer.Write(col.B);
					if (needAlpha) writer.Write(col.A);
				}

				prev = col;
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

				while (idx < lastIdx) {
					try {
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
					} catch (EndOfStreamException err) {
						throw new Exception("Ran out file! We were at index " + idx);
					}
				}

				// Create a document from the image
				doc = Document.FromImage(bmp);
			} catch (Exception err) {
				doc?.Dispose();
				throw new FormatException("Error loading file - " + err.Message, err);
			}

			return doc;
		}
	}
}
