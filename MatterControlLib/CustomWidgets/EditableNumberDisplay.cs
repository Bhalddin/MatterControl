﻿using System;
using MatterHackers.Agg;
using MatterHackers.Agg.UI;
using MatterHackers.VectorMath;

namespace MatterHackers.MatterControl
{
	public class EditableNumberDisplay : FlowLayoutWidget
	{
		protected GuiWidget clickableValueContainer;
		protected MHNumberEdit numberInputField;
		protected TextWidget valueDisplay;
		public string DisplayFormat { get; set; } = "{0}";
		public event EventHandler ValueChanged;

		public Color TextColor
		{
			get => valueDisplay.TextColor;
			set => valueDisplay.TextColor = value;
		}

		Color _borderColor = Color.White;
		private ThemeConfig theme;

		public override Color BorderColor
		{
			get { return _borderColor; }
			set
			{
				_borderColor = value;
				clickableValueContainer.BorderColor = new Color(BorderColor, 140);
			}
		}

		public EditableNumberDisplay(double startingValue, string largestPossibleValue, ThemeConfig theme)
			: this(startingValue, largestPossibleValue, theme.TextColor, theme)
		{
		}

		public EditableNumberDisplay(double startingValue, string largestPossibleValue, Color textColor, ThemeConfig theme)
			: base(FlowDirection.LeftToRight)
		{
			this.Margin = new BorderDouble(3, 0);
			this.VAnchor = VAnchor.Center;
			this.theme = theme;

			clickableValueContainer = new GuiWidget()
			{
				VAnchor = VAnchor.Stretch,
				Cursor = Cursors.Hand,
				Border = 1,
				BorderColor = BorderColor,
			};

			clickableValueContainer.MouseEnterBounds += (sender, e) =>
			{
				clickableValueContainer.BorderColor = new Color(BorderColor, 255);
			};

			clickableValueContainer.MouseLeaveBounds += (sender, e) =>
			{
				clickableValueContainer.BorderColor = new Color(BorderColor, 140);
			};

			valueDisplay = new TextWidget(largestPossibleValue, pointSize: 12)
			{
				TextColor = textColor,
				VAnchor = VAnchor.Center,
				HAnchor = HAnchor.Left,
				Margin = new BorderDouble(6),
			};

			clickableValueContainer.Click += editField_Click;

			clickableValueContainer.AddChild(valueDisplay);
			clickableValueContainer.SetBoundsToEncloseChildren();

			numberInputField = new MHNumberEdit(0, theme, pixelWidth: 40, allowDecimals: true)
			{
				VAnchor = VAnchor.Center,
				Margin = new BorderDouble(left: 6),
				Visible = false
			};

			// This is a hack to make sure the control is tall enough.
			// TODO: This hack needs a unit test and then pass and then remove this line.
			this.MinimumSize = new Vector2(0, numberInputField.Height);

			numberInputField.ActuallNumberEdit.EnterPressed += (s, e) => UpdateDisplayString();
			numberInputField.ContainsFocusChanged += (s1, e1) =>
			{
				if (!numberInputField.ContainsFocus)
				{
					UpdateDisplayString();
				}
			};

			numberInputField.KeyDown += (sender, e) =>
			{
				if (e.KeyCode == Keys.Escape)
				{
					clickableValueContainer.Visible = true;
					numberInputField.Visible = false;
				}
			};

			this.AddChild(clickableValueContainer);
			this.AddChild(numberInputField);

			Value = startingValue + 1;
			Value = startingValue;
			BorderColor = TextColor;
		}

		private void editField_Click(object sender, EventArgs mouseEvent)
		{
			clickableValueContainer.Visible = false;
			numberInputField.Visible = true;

			// This is trying to get all the numbers to the left of the decimal. We could do a better
			// job of finding the number. 6546431321654
			string displayString = valueDisplay.Text.Split('.')[0];
			if (displayString != null && displayString != "")
			{
				double displayStringAsValue;
				double.TryParse(displayString, out displayStringAsValue);
				numberInputField.ActuallNumberEdit.Value = displayStringAsValue;
			}

			numberInputField.ActuallNumberEdit.InternalNumberEdit.Focus();
			numberInputField.ActuallNumberEdit.InternalNumberEdit.SelectAll();
		}

		private void UpdateDisplayString(bool callValueChanged = true)
		{
			clickableValueContainer.Visible = true;
			numberInputField.Visible = false;
			valueDisplay.Text = string.Format(DisplayFormat, numberInputField.Value);
			if (callValueChanged)
			{
				ValueChanged?.Invoke(this, null);
			}
		}

		public double ValueDirect
		{
			set
			{
				double sameError = .00001;
				if (value < numberInputField.Value - sameError
					|| value > numberInputField.Value + sameError)
				{
					numberInputField.Value = value;
					UpdateDisplayString(false);
				}
			}
		}

		public double Value
		{
			get
			{
				return numberInputField.ActuallNumberEdit.Value;
			}

			set
			{
				double sameError = .00001;
				if (value < numberInputField.Value - sameError
					|| value > numberInputField.Value + sameError)
				{
					numberInputField.Value = value;
					UpdateDisplayString();
				}
			}
		}
	}
}