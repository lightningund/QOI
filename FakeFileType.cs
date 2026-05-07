// Originally from:
// https://github.com/sbtrn-devil/pdn-json/blob/main/PdnJsonFileType.cs
// Acquired on 06/05/25

// Paint dot net plugin to save and load a custom fake format of images:
// First two bytes are the width and height
// After that it's literally just the image data in 8bit RGB

// Run with `dotnet build`. It places the dll directly into the FileTypes folder so the terminal needs to be admin and Paint.NET needs to be closed
// To place it locally just change `<OutputPath>` to be a local folder, say `result`

using PaintDotNet;
using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;

namespace FakeFileType {
	public sealed class FakeFileTypeFactory : IFileTypeFactory {
		public FileType[] GetFileTypeInstances() {
			return [new FakeFileTypePlugin()];
		}
	}

	[PluginSupportInfo(typeof(PluginSupportInfo))]
	internal class FakeFileTypePlugin : FileType {
		/// <summary>
		/// Constructs a FakeFileTypePlugin instance
		/// </summary>
		internal FakeFileTypePlugin()
			: base(
				"Fake",
				new FileTypeOptions{
					LoadExtensions = [".fake"],
					SaveExtensions = [".fake"]
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
				byte width = reader.ReadByte();
				byte height = reader.ReadByte();
				byte[] imageData = new byte[width * height * 3];
				reader.Read(imageData);
				using Bitmap bmp = new(width, height, PixelFormat.Format24bppRgb);
				// Lock the bitmap's bits.
				BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, bmp.PixelFormat);

				// Copy the pixel data to the bitmap
				IntPtr ptr = bmpData.Scan0;
				System.Runtime.InteropServices.Marshal.Copy(imageData, 0, ptr, imageData.Length);

				// Unlock the bits.
				bmp.UnlockBits(bmpData);

				// Create a document from it
				doc = Document.FromImage(bmp);
			} catch (Exception e) {
				doc?.Dispose();
				throw new FormatException("Error loading file - " + e.Message, e);
			}

			return doc;
		}
	}
}
