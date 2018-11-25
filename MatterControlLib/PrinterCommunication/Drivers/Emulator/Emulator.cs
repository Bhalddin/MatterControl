﻿// Copyright (c) 2018, Lars Brubaker, John Lewin
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
//
// 1. Redistributions of source code must retain the above copyright notice, this
// list of conditions and the following disclaimer.
// 2. Redistributions in binary form must reproduce the above copyright notice,
// this list of conditions and the following disclaimer in the documentation
// and/or other materials provided with the distribution.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
// ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
// ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
// (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
// LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
// ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
// SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MatterHackers.SerialPortCommunication.FrostedSerial;

namespace MatterHackers.PrinterEmulator
{
	public partial class Emulator : IDisposable
	{
		/// <summary>
		/// The number of seconds the emulator should take to heat up to a given target
		/// </summary>
		public static double DefaultHeatUpTime = 3;

		public int CDChangeCount;
		public bool CDState;
		public int CtsChangeCount;
		public bool CtsState;
		public int DsrChangeCount;
		public bool DsrState;
		private static Regex numberRegex = new Regex(@"[-+]?[0-9]*\.?[0-9]+([eE][-+]?[0-9]+)?");

		private long commandIndex = 1;

		private int recievedCount = 0;

		// Dictionary of command and response callback
		private Dictionary<string, Func<string, string>> responses;

		private bool shuttingDown = false;

		public Emulator()
		{
			Emulator.Instance = this;

			responses = new Dictionary<string, Func<string, string>>()
			{
				{ "A",    Echo },
				{ "G0",   SetPosition },
				{ "G1",   SetPosition },
				{ "G28",  HomePosition },
				{ "G30",  SimulateProbe },
				{ "G4",   Wait },
				{ "G92",  ResetPosition },
				{ "M104", SetExtruderTemperature },
				{ "M105", ReturnTemp },
				{ "M106", SetFan },
				{ "M109", SetExtruderTemperature },
				{ "M110", SetLineCount },
				{ "M114", GetPosition },
				{ "M115", ReportMarlinFirmware },
				{ "M140", SetBedTemperature },
				{ "M190", SetBedTemperature },
				{ "M20",  ListSdCard },
				{ "M21",  InitSdCard },
				{ "N",    ParseChecksumLine },
				{ "T",    SetExtruderIndex },
			};
		}

		public event EventHandler ExtruderIndexChanged;

		public event EventHandler ExtruderTemperatureChanged;

		public event EventHandler FanSpeedChanged;

		public event EventHandler ZPositionChanged;
		public event EventHandler EPositionChanged;
		public event EventHandler<string> RecievedInstruction;

		// Instance reference allows test to access the most recently initialized emulator
		public static Emulator Instance { get; private set; }

		public Heater CurrentExtruder { get { return Extruders[ExtruderIndex]; } }
		public int ExtruderIndex { get; private set; }

		public List<Heater> Extruders { get; private set; } = new List<Heater>()
		{
			new Heater("Hotend1") { CurrentTemperature = 27 }
		};

		public double FanSpeed { get; private set; }
		public bool HasHeatedBed { get; set; } = true;
		public Heater HeatedBed { get; } = new Heater("HeatedBed") { CurrentTemperature = 26 };
		public string PortName { get; set; }

		public bool RunSlow { get; set; } = false;

		public bool SimulateLineErrors { get; set; } = false;
		public double XPosition { get; private set; }
		public double YPosition { get; private set; }
		public double ZPosition { get; private set; }

		public static int CalculateChecksum(string commandToGetChecksumFor)
		{
			int checksum = 0;
			if (commandToGetChecksumFor.Length > 0)
			{
				checksum = commandToGetChecksumFor[0];
				for (int i = 1; i < commandToGetChecksumFor.Length; i++)
				{
					checksum ^= commandToGetChecksumFor[i];
				}
			}
			return checksum;
		}

		public static bool GetFirstNumberAfter(string stringToCheckAfter, string stringWithNumber, ref double readValue, int startIndex = 0)
		{
			int stringPos = stringWithNumber.IndexOf(stringToCheckAfter, startIndex);
			if (stringPos != -1)
			{
				stringPos += stringToCheckAfter.Length;
				readValue = ParseDouble(stringWithNumber, ref stringPos);

				return true;
			}

			return false;
		}

		public static double ParseDouble(String source, ref int startIndex)
		{
			Match numberMatch = numberRegex.Match(source, startIndex);
			String returnString = numberMatch.Value;
			startIndex = numberMatch.Index + numberMatch.Length;
			double returnVal;
			double.TryParse(returnString, NumberStyles.Number, CultureInfo.InvariantCulture, out returnVal);
			return returnVal;
		}

		public void Dispose()
		{
			this.ShutDown();
			Emulator.Instance = null;
		}

		public string Echo(string command)
		{
			return command;
		}

		public string GetCommandKey(string command)
		{
			if (command.IndexOf(' ') != -1)
			{
				return command.Substring(0, command.IndexOf(' '));
			}
			return command;
		}

		public string GetCorrectResponse(string inCommand)
		{
			try
			{
				RecievedInstruction?.Invoke(this, inCommand);

				// Remove line returns
				var commandNoNl = inCommand.Split('\n')[0]; // strip of the trailing cr (\n)
				var command = ParseChecksumLine(commandNoNl);
				if (command.Contains("Resend"))
				{
					return command + "ok\n";
				}

				if (!command.StartsWith("G0")
					&& !command.StartsWith("G1")
					&& !command.StartsWith("M105"))
				{
					// Log non-busy commands
					Console.WriteLine(command);
				}

				var commandKey = GetCommandKey(command);
				if (responses.ContainsKey(commandKey))
				{
					if (RunSlow)
					{
						// do the right amount of time for the given command
						Thread.Sleep(20);
					}

					return responses[commandKey](command);
				}
				else
				{
					// Too noisy... restore if needed when debugging emulator
					//Console.WriteLine($"Command {command} not found");
				}
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
			}

			return "ok\n";
		}

		public string GetPosition(string command)
		{
			// position commands look like this: X:0.00 Y:0.00 Z0.00 E:0.00 Count X: 0.00 Y:0.00 Z:0.00 then an ok on the next line
			return $"X:{XPosition:0.00} Y: {YPosition:0.00} Z: {ZPosition:0.00} E: {CurrentExtruder.EPosition:0.00} Count X: 0.00 Y: 0.00 Z: 0.00\nok\n";
		}

		public string ReportMarlinFirmware(string command)
		{
			return "FIRMWARE_NAME:Marlin V1; Sprinter/grbl mashup for gen6 FIRMWARE_URL:https://github.com/MarlinFirmware/Marlin PROTOCOL_VERSION:1.0 MACHINE_TYPE:Framelis v1 EXTRUDER_COUNT:1 UUID:155f84b5-d4d7-46f4-9432-667e6876f37a\nok\n";
		}

		// Add response callbacks here
		public string ReturnTemp(string command)
		{
			// temp commands look like this: ok T:19.4 /0.0 B:0.0 /0.0 @:0 B@:0
			string response = "ok";
			for(int i=0; i<Extruders.Count; i++)
			{
				string TString = (Extruders.Count == 1) ? "T" : $"T{i}";
				response += $" {TString}:{Extruders[i].CurrentTemperature:0.0} / {Extruders[i].TargetTemperature:0.0}";
			}
			// Newline if HeatedBed is disabled otherwise HeatedBed stats
			response += ((!this.HasHeatedBed) ? "\n" : $" B: {HeatedBed.CurrentTemperature:0.0} / {HeatedBed.TargetTemperature:0.0}\n");
			return response;
		}

		public string SetFan(string command)
		{
			try
			{
				var sIndex = command.IndexOf('S') + 1;

				string fanSpeed = command.Substring(sIndex);

				int spaceIndex = fanSpeed.IndexOf(' ');
				if (spaceIndex != -1)
				{
					fanSpeed = fanSpeed.Substring(0, spaceIndex);
				}

				FanSpeed = int.Parse(fanSpeed);
				FanSpeedChanged?.Invoke(this, null);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
			}

			return "ok\n";
		}

		public void ShutDown()
		{
			if (!shuttingDown)
			{
				shuttingDown = true;

				HeatedBed.Stop();
				foreach (var extuder in Extruders)
				{
					extuder.Stop();
				}
			}
		}

		public void SimulateReboot()
		{
			commandIndex = 1;
			recievedCount = 0;
		}

		private string HomePosition(string command)
		{
			XPosition = 0;
			YPosition = 0;
			ZPosition = 0;
			return "ok\n";
		}

		private Random rand = new Random();

		private string SimulateProbe(string command)
		{
			Thread.Sleep(500);
			return "Bed Position X: 0 Y: 0 Z: { rand.NextDouble() }\n"
				 + "ok\n";
		}

		private string InitSdCard(string arg)
		{
			return "ok\n";
		}

		private string ListSdCard(string arg)
		{
			string[] responsList =
			{
				"Begin file list",
				"Item 1.gcode",
				"Item 2.gcode",
				"End file list",
			};

			foreach (var response in responsList)
			{
				this.QueueResponse(response + '\n');
			}

			return "ok\n";
		}

		private string ParseChecksumLine(string command)
		{
			recievedCount++;
			if (SimulateLineErrors && (recievedCount % 11) == 0)
			{
				command = "N-1 nthoeuc 654*";
			}

			if (!string.IsNullOrEmpty(command) && command[0] == 'N')
			{
				double lineNumber = 0;
				GetFirstNumberAfter("N", command, ref lineNumber);
				var checksumStart = command.LastIndexOf('*');
				var commandToChecksum = command.Substring(0, checksumStart);
				if (commandToChecksum[commandToChecksum.Length - 1] == ' ')
				{
					commandToChecksum = commandToChecksum.Substring(0, commandToChecksum.Length - 1);
				}
				double expectedChecksum = 0;
				GetFirstNumberAfter("*", command, ref expectedChecksum, checksumStart);
				int actualChecksum = CalculateChecksum(commandToChecksum);
				if ((lineNumber == commandIndex
					&& actualChecksum == expectedChecksum)
					|| command.Contains("M110"))
				{
					commandIndex++;
					int spaceIndex = command.IndexOf(' ') + 1;
					int endIndex = command.IndexOf('*');
					return command.Substring(spaceIndex, endIndex - spaceIndex);
				}
				else
				{
					return $"Error:checksum mismatch, Last Line: {commandIndex - 1}\nResend: {commandIndex}\n";
				}
			}
			else
			{
				return command;
			}
		}

		private string ResetPosition(string command)
		{
			double value = 0;
			if (GetFirstNumberAfter("X", command, ref value))
			{
				XPosition = value;
			}
			if (GetFirstNumberAfter("Y", command, ref value))
			{
				YPosition = value;
			}
			if (GetFirstNumberAfter("Z", command, ref value))
			{
				ZPosition = value;
				ZPositionChanged?.Invoke(null, null);
			}
			if (GetFirstNumberAfter("E", command, ref value))
			{
				CurrentExtruder.LastEPosition = value;
				CurrentExtruder.EPosition = value;
				EPositionChanged?.Invoke(null, null);
			}

			return "ok\n";
		}

		private string SetBedTemperature(string command)
		{
			try
			{
				// M140 S210 or M190 S[temp]
				var sIndex = command.IndexOf('S') + 1;

				string temperature = command.Substring(sIndex);

				int spaceIndex = temperature.IndexOf(' ');
				if (spaceIndex != -1)
				{
					temperature = temperature.Substring(0, spaceIndex);
				}

				HeatedBed.TargetTemperature = int.Parse(temperature);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
			}

			return "ok\n";
		}

		private string SetExtruderIndex(string command)
		{
			try
			{
				// T0, T1, T2 are the expected format
				var tIndex = command.IndexOf('T') + 1;
				var extruderIndex = command.Substring(tIndex);

				ExtruderIndex = int.Parse(extruderIndex);
				ExtruderIndexChanged?.Invoke(this, null);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
			}

			return "ok\n";
		}

		private string SetExtruderTemperature(string command)
		{
			try
			{
				// M104 S210 or M109 S[temp]

				double index = 0;
				GetFirstNumberAfter("T", command, ref index);

				double temp = 0;
				GetFirstNumberAfter("S", command, ref temp);

				if (index > Extruders.Count - 1)
				{
					// increase the number of extruders
					var newList = new List<Heater>(Extruders.Count + 1);
					foreach(var extruder in Extruders)
					{
						newList.Add(extruder);
					}

					for(int i=Extruders.Count+1; i<index+2; i++)
					{
						newList.Add(new Heater($"Hotend{i}") { CurrentTemperature = 27 });
					}
					Extruders = newList;
				}

				Extruders[(int)index].TargetTemperature = temp;
				ExtruderTemperatureChanged?.Invoke(this, null);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
			}

			return "ok\n";
		}

		private string SetLineCount(string command)
		{
			double number = commandIndex;
			if (GetFirstNumberAfter("N", command, ref number))
			{
				commandIndex = (long)number + 1;
			}

			return "ok\n";
		}

		private string SetPosition(string command)
		{
			double value = 0;
			if (GetFirstNumberAfter("X", command, ref value))
			{
				XPosition = value;
			}
			if (GetFirstNumberAfter("Y", command, ref value))
			{
				YPosition = value;
			}
			if (GetFirstNumberAfter("Z", command, ref value))
			{
				ZPosition = value;
				ZPositionChanged?.Invoke(null, null);
			}
			if (GetFirstNumberAfter("E", command, ref value))
			{
				CurrentExtruder.LastEPosition = CurrentExtruder.EPosition;
				CurrentExtruder.EPosition = value;
				CurrentExtruder.AbsoluteEPosition += CurrentExtruder.EPosition - CurrentExtruder.LastEPosition;
				EPositionChanged?.Invoke(null, null);
			}

			return "ok\n";
		}

		private string Wait(string command)
		{
			try
			{
				// M140 S210 or M190 S[temp]
				double timeToWait = 0;
				if (!GetFirstNumberAfter("S", command, ref timeToWait))
				{
					if (GetFirstNumberAfter("P", command, ref timeToWait))
					{
						timeToWait /= 1000;
					}
				}

				Thread.Sleep((int)(timeToWait * 1000));
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
			}

			return "ok\n";
		}
	}

	// EmulatorPort
	public partial class Emulator : IFrostedSerialPort
	{
		private object receiveLock = new object();
		private Queue<string> receiveQueue = new Queue<string>();
		private AutoResetEvent receiveResetEvent = new AutoResetEvent(false);
		private object sendLock = new object();
		private Queue<string> sendQueue = new Queue<string>(new string[] { "Emulator v0.1\n" });
		public int BaudRate { get; set; }

		public int BytesToRead
		{
			get
			{
				if (sendQueue.Count == 0)
				{
					return 0;
				}

				return sendQueue?.Peek().Length ?? 0;
			}
		}

		public bool DtrEnable { get; set; }
		public bool IsOpen { get; private set; }
		public int ReadTimeout { get; set; }
		public bool RtsEnable { get; set; }
		public int WriteTimeout { get; set; }

		public void Close()
		{
			this.ShutDown();
		}

		public void Open()
		{
			this.IsOpen = true;

			receiveResetEvent = new AutoResetEvent(false);

			this.ReadTimeout = 500;
			this.WriteTimeout = 500;

			Console.WriteLine("\n Initializing emulator (Speed: {0})", (this.RunSlow) ? "slow" : "fast");

			Task.Run(() =>
			{
				Thread.CurrentThread.Name = "EmulatorDtr";
				while (!shuttingDown)
				{
					if (this.DtrEnable != DsrState)
					{
						DsrState = this.DtrEnable;
						DsrChangeCount++;
					}

					Thread.Sleep(10);
				}
			});

			Task.Run(() =>
			{
				Thread.CurrentThread.Name = "EmulatorPipeline";

				while (!shuttingDown || receiveQueue.Count > 0)
				{
					if (receiveQueue.Count == 0)
					{
						if (shuttingDown)
						{
							return;
						}

						receiveResetEvent.WaitOne();
					}

					if (receiveQueue.Count == 0)
					{
						if (shuttingDown)
						{
							return;
						}

						Thread.Sleep(10);
					}
					else
					{
						string receivedLine;

						lock (receiveLock)
						{
							receivedLine = receiveQueue.Dequeue();
						}

						if (receivedLine?.Length > 0)
						{
							//Thread.Sleep(250);
							string emulatedResponse = GetCorrectResponse(receivedLine);

							lock (sendLock)
							{
								sendQueue.Enqueue(emulatedResponse);
							}
						}
					}
				}

				this.IsOpen = false;

				this.Dispose();
			});
		}

		public void QueueResponse(string line)
		{
			sendQueue.Enqueue(line);
		}

		public int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();

		public string ReadExisting()
		{
			lock (sendLock)
			{
				return sendQueue.Dequeue();
			}
		}

		public void Write(string receivedLine)
		{
			lock (receiveLock)
			{
				receiveQueue.Enqueue(receivedLine);
			}

			// Release the main loop to process the received command
			receiveResetEvent.Set();
		}

		public void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();
	}
}