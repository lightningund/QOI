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
			byte width = (byte)input.Width;
			byte height = (byte)input.Height;
			writer.Write(width);
			writer.Write(height);
			Surface boring = new(width, height);
			input.Flatten(boring);
			for (int j = 0; j < height; ++j) {
				for (int i = 0; i < width; ++i) {
					var col = boring[i, j];
					writer.Write(col.B);
					writer.Write(col.G);
					writer.Write(col.R);
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
				// byte width = reader.ReadByte();
				// byte height = reader.ReadByte();

				var magic = reader.ReadChars(4);

				// Read bytes into the header struct
				var header = ReadType<Header>(reader);

				if (BitConverter.IsLittleEndian) {
					header.width = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(header.width);
					header.height = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(header.height);
				}

				throw new Exception(new string(magic) + ", " + header.width + ", " + header.height);

				// byte[] imageData = new byte[width * height * 3];
				// reader.Read(imageData);
				// using Bitmap bmp = new(width, height, PixelFormat.Format24bppRgb);

				// BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, bmp.PixelFormat);

				// // Copy the pixel data to the bitmap
				// IntPtr ptr = bmpData.Scan0;
				// Marshal.Copy(imageData, 0, ptr, imageData.Length);

				// bmp.UnlockBits(bmpData);

				// // Create a document from the image
				// doc = Document.FromImage(bmp);
			} catch (Exception e) {
				doc?.Dispose();
				throw new FormatException("Error loading file - " + e.Message, e);
			}

			return doc;
		}
	}

	internal struct Header {
		public uint width;
		public uint height;
		public byte channels;
		public byte colorspace;
	}
}
