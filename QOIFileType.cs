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

// Currently going to implement a way simpler file type that's not real
// First two bytes are the width and height
// After that it's literally just the image data in 8bit RGB

namespace QOIFileType {
	public sealed class QOIFileTypeFactory : IFileTypeFactory {
		public FileType[] GetFileTypeInstances() {
			return [new QOIFileTypePlugin()];
		}
	}

	[PluginSupportInfo(typeof(PluginSupportInfo))]
	internal class QOIFileTypePlugin : FileType {
		// TODO:
		private const string HeaderSignature = ".QOI";

		/// <summary>
		/// Constructs a ExamplePropertyBasedFileType instance
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
			using (var writer = new BinaryWriter(output)) {
				byte width = (byte)input.Width;
				byte height = (byte)input.Height;
				writer.Write(width);
				writer.Write(height);
				Document boring = input.Flatten();
				BitmapLayer bmpLayer = boring.Layers[0] as BitmapLayer;
				using (Bitmap bmp = bmpLayer.Surface.CreateAliasedBitmap()) {
					bmp.Save(output, ImageFormat.Bmp);
				}
			}
		}

		/// <summary>
		/// Creates a document from a stream
		/// </summary>
		protected override Document OnLoad(Stream input) {
			Document doc = null;

			try {
				using (var reader = new BinaryReader(input)) {
					byte width = reader.ReadByte();
					byte height = reader.ReadByte();
					byte[] imageData = new byte[width * height * 3];
					reader.Read(imageData);
					using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb)) {
						// Lock the bitmap's bits.
						BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, bmp.PixelFormat);

						// Copy the pixel data to the bitmap
						IntPtr ptr = bmpData.Scan0;
						System.Runtime.InteropServices.Marshal.Copy(imageData, 0, ptr, imageData.Length);

						// Unlock the bits.
						bmp.UnlockBits(bmpData);

						// Create a document from it
						doc = Document.FromImage(bmp);
					}
				}
			} catch (Exception e) {
				if (doc != null) doc.Dispose();
				throw new FormatException("Error loading file - " + e.Message, e);
			}

			return doc;
		}
	}
}
