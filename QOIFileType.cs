// Originally from:
// https://github.com/sbtrn-devil/pdn-json/blob/main/PdnJsonFileType.cs
// Acquired on 06/05/25

// Paint dot net plugin to save and load QOI images

// Run with `dotnet build`. It places the dll directly into the FileTypes folder so the terminal needs to be admin and Paint.NET needs to be closed
// To place it locally just change `<OutputPath>` to be a local folder, say `result`

using PaintDotNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Drawing;
using System.Drawing.Imaging;

// Currently going to implement a way simpler file type that's not real
// First two bytes are the width and height
// After that it's literally just the image data in 8bit RGB

namespace QOIFileType {
	public sealed class QOIFileTypeFactory : IFileTypeFactory {
		public FileType[] GetFileTypeInstances() {
			return new[] { new QOIFileTypePlugin() };
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
					LoadExtensions = new string[] { ".qoi" },
					SaveExtensions = new string[] { ".qoi" }
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
			PJSFile pjsFile = new PJSFile();
			pjsFile.width = input.Width;
			pjsFile.height = input.Height;
			pjsFile.dpuUnit = input.DpuUnit.ToString();
			pjsFile.dpuX = input.DpuX;
			pjsFile.dpuY = input.DpuY;

			foreach (Layer layer in input.Layers) {
				BitmapLayer pdnLayer = layer as BitmapLayer;
				PJSLayer pjsLayer = new PJSLayer();

				// transfer layer properties to its PJS representation
				pjsLayer.name = pdnLayer.Name;
				pjsLayer.width = pdnLayer.Width;
				pjsLayer.height = pdnLayer.Height;
				pjsLayer.blendMode = pdnLayer.BlendMode.ToString();
				pjsLayer.visible = pdnLayer.Visible;
				pjsLayer.opacity = pdnLayer.Opacity;

				// transfer the data (as mimeType + base64 encoded image file - we'll use PNG)
				pjsLayer.mimeType = "image/png";
				pjsLayer.base64 = "";

				using (MemoryStream bmpStream = new MemoryStream()) {
					using (Bitmap bmp = pdnLayer.Surface.CreateAliasedBitmap()) {
						bmp.Save(bmpStream, ImageFormat.Png);
					}

					pjsLayer.base64 = Convert.ToBase64String(bmpStream.ToArray());
				}

				pjsFile.layers.Add(pjsLayer);
			}

			// write the PJS representation
			DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(PJSFile));
			ser.WriteObject(output, pjsFile);
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
