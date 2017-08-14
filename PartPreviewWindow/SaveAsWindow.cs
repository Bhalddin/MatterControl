﻿/*
Copyright (c) 2017, Lars Brubaker, John Lewin
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

using System;
using System.IO;
using System.Threading.Tasks;
using MatterHackers.Agg;
using MatterHackers.Agg.UI;
using MatterHackers.Localizations;
using MatterHackers.MatterControl.CustomWidgets;
using MatterHackers.MatterControl.DataStorage;
using MatterHackers.MatterControl.Library;

namespace MatterHackers.MatterControl
{
	public class SaveAsContext : LibraryConfig
	{
	}

	public class SaveAsWindow : SystemWindow
	{
		private Func<SaveAsReturnInfo, Task> functionToCallOnSaveAs;
		private MHTextEditWidget textToAddWidget;
		private ListView librarySelectorWidget;
		private Button saveAsButton;

		private ILibraryContext libraryNavContext;

		public SaveAsWindow(Func<SaveAsReturnInfo, Task> functionToCallOnSaveAs, ILibraryContainer providerLocator, bool showQueue, bool getNewName)
			: base(480, 500)
		{
			var buttonFactory = ApplicationController.Instance.Theme.ButtonFactory;
			
			AlwaysOnTopOfMain = true;
			Title = "MatterControl - " + "Save As".Localize();
			this.Name = "Save As Window";
			this.functionToCallOnSaveAs = functionToCallOnSaveAs;

			FlowLayoutWidget topToBottom = new FlowLayoutWidget(FlowDirection.TopToBottom);
			topToBottom.AnchorAll();
			topToBottom.Padding = new BorderDouble(3, 0, 3, 5);

			// Creates Header
			FlowLayoutWidget headerRow = new FlowLayoutWidget(FlowDirection.LeftToRight)
			{
				HAnchor = HAnchor.Stretch,
				Margin = new BorderDouble(0, 3, 0, 0),
				Padding = new BorderDouble(0, 3, 0, 3)
			};

			BackgroundColor = ActiveTheme.Instance.PrimaryBackgroundColor;

			//Creates Text and adds into header
			TextWidget elementHeader = new TextWidget("Save New Design".Localize() + ":", pointSize: 14)
			{
				TextColor = ActiveTheme.Instance.PrimaryTextColor,
				HAnchor = HAnchor.Stretch,
				VAnchor = Agg.UI.VAnchor.Bottom
			};

			headerRow.AddChild(elementHeader);
			topToBottom.AddChild(headerRow);

			//Creates container in the middle of window
			FlowLayoutWidget middleRowContainer = new FlowLayoutWidget(FlowDirection.TopToBottom)
			{
				HAnchor = HAnchor.Stretch,
				VAnchor = VAnchor.Stretch,
				Padding = new BorderDouble(5),
				BackgroundColor = ActiveTheme.Instance.SecondaryBackgroundColor
			};

			libraryNavContext = new SaveAsContext()
			{
				ActiveContainer = ApplicationController.Instance.Library.RootLibaryContainer
			};
			libraryNavContext.ContainerChanged += (s, e) =>
			{
				saveAsButton.Enabled = libraryNavContext.ActiveContainer is ILibraryWritableContainer;
			};

			librarySelectorWidget = new ListView(libraryNavContext)
			{
				BackgroundColor = ActiveTheme.Instance.TertiaryBackgroundColor,
				ShowItems = false,
				ContainerFilter = (container) => !container.IsReadOnly
			};

			// put in the bread crumb widget
			var breadCrumbWidget = new FolderBreadCrumbWidget(librarySelectorWidget);
			middleRowContainer.AddChild(breadCrumbWidget);

			// put in the area to pick the provider to save to
			// Adds text box and check box to the above container
			GuiWidget chooseWindow = new GuiWidget(10, 30)
			{
				HAnchor = HAnchor.Stretch,
				VAnchor = VAnchor.Stretch,
				Margin = new BorderDouble(5),
				BackgroundColor = ActiveTheme.Instance.PrimaryBackgroundColor,
				Padding = new BorderDouble(3),
			};
			chooseWindow.AddChild(librarySelectorWidget);
			middleRowContainer.AddChild(chooseWindow);

			// put in the area to type in the new name
			if(getNewName)
			{
				TextWidget fileNameHeader = new TextWidget("Design Name".Localize(), pointSize: 12)
				{
					TextColor = ActiveTheme.Instance.PrimaryTextColor,
					Margin = new BorderDouble(5),
					HAnchor = HAnchor.Left
				};

				//Adds text box and check box to the above container
				textToAddWidget = new MHTextEditWidget("", pixelWidth: 300, messageWhenEmptyAndNotSelected: "Enter a Design Name Here".Localize())
				{
					HAnchor = HAnchor.Stretch,
					Margin = new BorderDouble(5)
				};
				textToAddWidget.ActualTextEditWidget.EnterPressed += new KeyEventHandler(ActualTextEditWidget_EnterPressed);

				middleRowContainer.AddChild(fileNameHeader);
				middleRowContainer.AddChild(textToAddWidget);
			}

			middleRowContainer.AddChild(new HorizontalSpacer());
			topToBottom.AddChild(middleRowContainer);

			//Creates button container on the bottom of window
			FlowLayoutWidget buttonRow = new FlowLayoutWidget(FlowDirection.LeftToRight);
			{
				BackgroundColor = ActiveTheme.Instance.PrimaryBackgroundColor;
				buttonRow.HAnchor = HAnchor.Stretch;
				buttonRow.Padding = new BorderDouble(0, 3);
			}

			saveAsButton = buttonFactory.Generate("Save".Localize());
			saveAsButton.Name = "Save As Save Button";
			// Disable the save as button until the user actually selects a provider
			saveAsButton.Enabled = false;
			saveAsButton.Cursor = Cursors.Hand;
			saveAsButton.Click += saveAsButton_Click;
			buttonRow.AddChild(saveAsButton);

			//Adds SaveAs and Close Button to button container
			buttonRow.AddChild(new HorizontalSpacer());

			Button cancelButton = buttonFactory.Generate("Cancel".Localize());
			cancelButton.Visible = true;
			cancelButton.Cursor = Cursors.Hand;
			buttonRow.AddChild(cancelButton);
			cancelButton.Click += (sender, e) =>
			{
				CloseOnIdle();
			};

			topToBottom.AddChild(buttonRow);

			this.AddChild(topToBottom);

			ShowAsSystemWindow();
		}

		public override void OnLoad(EventArgs args)
		{
			if (textToAddWidget != null
				&& !UserSettings.Instance.IsTouchScreen)
			{
				UiThread.RunOnIdle(textToAddWidget.Focus);
			}
			base.OnLoad(args);
		}

		private void ActualTextEditWidget_EnterPressed(object sender, KeyEventArgs keyEvent)
		{
			SubmitForm();
		}

		private void saveAsButton_Click(object sender, EventArgs mouseEvent)
		{
			SubmitForm();
		}

		private void SubmitForm()
		{
			string newName = "none";
			if (textToAddWidget != null)
			{
				newName = textToAddWidget.ActualTextEditWidget.Text;
			}

			if (newName != "")
			{
				string fileName = Path.ChangeExtension(Path.GetRandomFileName(), ".amf");
				string fileNameAndPath = Path.Combine(ApplicationDataStorage.Instance.ApplicationLibraryDataPath, fileName);

				functionToCallOnSaveAs(new SaveAsReturnInfo(newName, fileNameAndPath, librarySelectorWidget.ActiveContainer));

				CloseOnIdle();
			}
		}

		public class SaveAsReturnInfo
		{
			public string fileNameAndPath;
			public string newName;
			public ILibraryContainer DestinationContainer;

			public SaveAsReturnInfo(string newName, string fileNameAndPath, ILibraryContainer destinationLibraryProvider)
			{
				this.DestinationContainer = destinationLibraryProvider;
				this.newName = newName;
				this.fileNameAndPath = fileNameAndPath;
			}
		}
	}
}