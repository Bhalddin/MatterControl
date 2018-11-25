﻿/*
Copyright (c) 2015, Lars Brubaker
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies,
either expressed or implied, of the FreeBSD Project.
*/

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using MatterHackers.Agg.Image;
using MatterHackers.Agg.Platform;
using MatterHackers.Agg.UI;
using MatterHackers.MatterControl.PrinterControls;
using MatterHackers.MatterControl.SlicerConfiguration;

namespace MatterHackers.MatterControl.PrinterCommunication.Io
{
	public class QueuedCommandsStream : GCodeStreamProxy
	{
		private List<string> commandQueue = new List<string>();
		private object locker = new object();

		public QueuedCommandsStream(PrinterConfig printer, GCodeStream internalStream)
			: base(printer, internalStream)
		{
		}

		public int Count => commandQueue.Count;

		public void Add(string line, bool forceTopOfQueue = false)
		{
			// lock queue
			lock (locker)
			{
				if (forceTopOfQueue)
				{
					commandQueue.Insert(0, line);
				}
				else
				{
					commandQueue.Add(line);
				}
			}
		}

		public void Cancel()
		{
			Reset();
		}

		public ImageBuffer LoadImageAsset(string uri)
		{
			string filePath = Path.Combine("Images", "Macros", uri);
			bool imageOnDisk = false;
			if (uri.IndexOfAny(Path.GetInvalidFileNameChars()) == -1)
			{
				try
				{
					imageOnDisk = AggContext.StaticData.FileExists(filePath);
				}
				catch
				{
					imageOnDisk = false;
				}
			}

			if (imageOnDisk)
			{
				return AggContext.StaticData.LoadImage(filePath);
			}
			else
			{
				var imageBuffer = new ImageBuffer(320, 10);

				ApplicationController.Instance.DownloadToImageAsync(imageBuffer, uri, true);

				return imageBuffer;
			}
		}

		public override string ReadLine()
		{
			string lineToSend = null;

			{
				// lock queue
				lock (locker)
				{
					if (commandQueue.Count > 0)
					{
						lineToSend = commandQueue[0];
						lineToSend = printer.ReplaceMacroValues(lineToSend);
						commandQueue.RemoveAt(0);
					}
				}

				if (lineToSend == null)
				{
					lineToSend = base.ReadLine();
				}
			}

			return lineToSend;
		}

		public void Reset()
		{
			lock (locker)
			{
				commandQueue.Clear();
			}
		}
	}
}