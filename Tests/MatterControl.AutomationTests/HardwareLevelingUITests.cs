﻿using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace MatterHackers.MatterControl.Tests.Automation
{
	[TestFixture, Category("MatterControl.UI.Automation"), RunInApplicationDomain, Apartment(ApartmentState.STA)]
	public class HardwareLevelingUITests
	{
		[Test]
		public async Task HasHardwareLevelingHidesLevelingSettings()
		{
			await MatterControlUtilities.RunTest((testRunner) =>
			{
				testRunner.WaitForFirstDraw();

				// Add printer that has hardware leveling
				testRunner.AddAndSelectPrinter("Airwolf 3D", "HD");

				testRunner.SwitchToPrinterSettings();
				testRunner.ClickByName("Features Tab");
				testRunner.ClickByName("Slice Settings Overflow Menu");
				testRunner.ClickByName("Expand All Menu Item");
				Assert.IsFalse(testRunner.WaitForName("print_leveling_solution Row", .5), "Print leveling should not exist for an Airwolf HD");

				// Add printer that does not have hardware leveling
				testRunner.AddAndSelectPrinter("3D Factory", "MendelMax 1.5");

				testRunner.SwitchToPrinterSettings();
				testRunner.ClickByName("Features Tab");
				testRunner.ClickByName("Slice Settings Overflow Menu");
				testRunner.ClickByName("Expand All Menu Item");
				Assert.IsTrue(testRunner.WaitForName("print_leveling_solution Row"), "Print leveling should exist for a 3D Factory MendelMax");

				return Task.CompletedTask;
			}, overrideHeight: 800);
		}

		[Test, Category("Emulator")]
		public async Task SoftwareLevelingRequiredCorrectWorkflow()
		{
			await MatterControlUtilities.RunTest((testRunner) =>
			{
				// make a jump start printer
				using (var emulator = testRunner.LaunchAndConnectToPrinterEmulator("JumpStart", "V1", runSlow: false))
				{
					// make sure it is showing the correct button
					Assert.IsFalse(testRunner.WaitForName("PrintPopupMenu", .5), "Start Print should not be visible if PrintLeveling is required");
					Assert.IsTrue(testRunner.WaitForName("Finish Setup Button"), "Finish Setup should be visible if PrintLeveling is required");

					// HACK: automatically resuming setup wizard. Long term we want a better plan
					testRunner.ClickByName("Finish Setup Button");

					testRunner.Complete9StepLeveling();

					// make sure the button has changed to start print
					Assert.IsTrue(testRunner.WaitForName("PrintPopupMenu"), "Start Print should be visible after leveling the printer");
					Assert.IsFalse(testRunner.WaitForName("Finish Setup Button", .5), "Finish Setup should not be visible after leveling the printer");

					// reset to defaults and make sure print leveling is cleared
					testRunner.SwitchToSliceSettings();

					testRunner.ClickByName("Printer Overflow Menu");
					testRunner.ClickByName("Reset to Defaults Menu Item");
					testRunner.ClickByName("Yes Button");

					// make sure it is showing the correct button
					Assert.IsTrue(testRunner.WaitForName("Finish Setup Button"), "Finish Setup should be visible after reset to Defaults");
					Assert.IsTrue(!testRunner.WaitForName("PrintPopupMenu"), "Start Print should not be visible after reset to Defaults");
				}

				return Task.CompletedTask;
			});
		}
	}
}

