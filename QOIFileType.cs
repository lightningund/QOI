// Originally from:
// https://github.com/sbtrn-devil/pdn-json/blob/main/PdnJsonFileType.cs
// Acquired on 06/05/25

// Paint dot net plugin to save and load QOI images

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
		private const string HeaderSignature = ".PDN";

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
				using (var reader = new BinaryReader(stream)) {
					byte width = reader.ReadByte();
					byte height = reader.ReadByte();
					// doc = new Document(width, height);
					byte[] imageData = new byte[width * height * 3];
					reader.Read(imageData);
					using (Stream imageDataStream = new MemoryStream(imageData)) {
						using (Image image = Image.FromStream(imageDataStream)) {
							doc = Document.FromGdipImage(image);
							// Surface surface = Surface.CopyFromGdipImage(image);
							// // construct BitmapLayer from Surface that will also take its ownership
							// BitmapLayer pdnLayer = new BitmapLayer(surface, true);

							// // the layer is ready
							// doc.Layers.Add(pdnLayer);
						}
					}
				}
			} catch (Exception e) {
				if (doc != null) {
					doc.Dispose();
				}
				throw new FormatException("Error loading file - " + e.Message, e);
			}

			return doc;
		}
	}

	[DataContract]
	internal class PJSLayer {
		[DataMember] internal int width;
		[DataMember] internal int height;
		[DataMember] internal bool visible;
		[DataMember] internal byte opacity;
		[DataMember] internal String name;
		[DataMember] internal String blendMode;
		[DataMember] internal String mimeType;
		[DataMember] internal String base64;
	}

	[DataContract]
	internal class PJSFile {
		[DataMember] internal HashSet<String> features = new HashSet<String>();
		[DataMember] internal int width;
		[DataMember] internal int height;
		[DataMember(IsRequired=false)] internal String dpuUnit { get; set; }
		[DataMember(IsRequired=false)] internal double? dpuX { get; set; }
		[DataMember(IsRequired=false)] internal double? dpuY { get; set; }

		[DataMember]
		internal List<PJSLayer> layers = new List<PJSLayer>();

		[System.Runtime.Serialization.OnDeserialized]
		void OnDeserialized(System.Runtime.Serialization.StreamingContext c) {
			dpuUnit = (dpuUnit == null ? "Inch" : dpuUnit);
			dpuX = (dpuX == null ? 96.0 : dpuX);
			dpuY = (dpuY == null ? 96.0 : dpuY);
		}
	}

	internal static class Features {
		// any strings that can go to "features" array are to be defined and referenced via this class
		internal const String RESERVED = "RESERVED";
	}
}
