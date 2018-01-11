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
using MatterHackers.Agg;
using MatterHackers.Agg.Platform;
using MatterHackers.Agg.UI;
using MatterHackers.Localizations;
using MatterHackers.MatterControl.CustomWidgets;
using MatterHackers.MatterControl.PrinterCommunication;
using MatterHackers.MatterControl.PrinterControls;
using MatterHackers.MatterControl.SlicerConfiguration;

namespace MatterHackers.MatterControl
{
	public static class EnabledWidgetExtensions
	{
		public static void SetEnabled(this GuiWidget guiWidget, bool enabled)
		{
			guiWidget.Enabled = enabled;
		}
	}

	public class ManualPrinterControls : GuiWidget
	{
		static public RootedObjectEventHandler AddPluginControls = new RootedObjectEventHandler();

		private static bool pluginsQueuedToAdd = false;
		private PrinterConfig printer;

		public ManualPrinterControls(PrinterConfig printer)
		{
			this.printer = printer;
			this.BackgroundColor = ApplicationController.Instance.Theme.TabBodyBackground;
			this.AnchorAll();
			this.AddChild(new ManualPrinterControlsDesktop(printer));
		}

		public override void OnLoad(EventArgs args)
		{
			if (!pluginsQueuedToAdd && printer.Settings.GetValue(SettingsKey.include_firmware_updater) == "Simple Arduino")
			{
				UiThread.RunOnIdle(() =>
				{
					AddPluginControls.CallEvents(this, null);
					pluginsQueuedToAdd = false;
				});
				pluginsQueuedToAdd = true;
			}

			base.OnLoad(args);
		}
	}

	public class ManualPrinterControlsDesktop : ScrollableWidget
	{
		private GuiWidget fanControlsContainer;
		private GuiWidget macroControlsContainer;
		private GuiWidget tuningAdjustmentControlsContainer;
		private MovementControls movementControlsContainer;
		private GuiWidget calibrationControlsContainer;

		private EventHandler unregisterEvents;
		private PrinterConfig printer;

		public ManualPrinterControlsDesktop(PrinterConfig printer)
		{
			var theme = ApplicationController.Instance.Theme;

			this.printer = printer;
			this.ScrollArea.HAnchor |= HAnchor.Stretch;
			this.AnchorAll();
			this.AutoScroll = true;
			this.HAnchor = HAnchor.Stretch;
			this.VAnchor = VAnchor.Stretch;
			this.Padding = new BorderDouble(8, 0, theme.ToolbarPadding.Right, 6);

			int headingPointSize = theme.H1PointSize;

			var controlsTopToBottomLayout = new FlowLayoutWidget(FlowDirection.TopToBottom)
			{
				HAnchor = HAnchor.MaxFitOrStretch,
				VAnchor = VAnchor.Fit,
				Name = "ManualPrinterControls.ControlsContainer",
				Margin = new BorderDouble(0)
			};
			this.AddChild(controlsTopToBottomLayout);

			SectionWidget sectionWidget;

			sectionWidget = MovementControls.CreateSection(printer, theme);
			controlsTopToBottomLayout.AddChild(sectionWidget);
			movementControlsContainer = sectionWidget.ContentPanel as MovementControls;

			if (!printer.Settings.GetValue<bool>(SettingsKey.has_hardware_leveling))
			{
				sectionWidget = CalibrationControls.CreateSection(printer, theme);
				controlsTopToBottomLayout.AddChild(sectionWidget);
				calibrationControlsContainer = sectionWidget.ContentPanel;
			}

			sectionWidget = MacroControls.CreateSection(printer, theme);
			controlsTopToBottomLayout.AddChild(sectionWidget);
			macroControlsContainer = sectionWidget.ContentPanel;

			if (printer.Settings.GetValue<bool>(SettingsKey.has_fan))
			{
				sectionWidget = FanControls.CreateSection(printer, theme);
				controlsTopToBottomLayout.AddChild(sectionWidget);
				fanControlsContainer = sectionWidget.ContentPanel;
			}

#if !__ANDROID__
			sectionWidget = PowerControls.CreateSection(printer, theme);
			controlsTopToBottomLayout.AddChild(sectionWidget);
#endif

			sectionWidget = AdjustmentControls.CreateSection(printer, theme);
			controlsTopToBottomLayout.AddChild(sectionWidget);
			tuningAdjustmentControlsContainer = sectionWidget.ContentPanel;


			// Enforce panel padding in sidebar
			foreach (var widget in controlsTopToBottomLayout.Children<SectionWidget>())
			{
				var contentPanel = widget.ContentPanel;
				contentPanel.Padding = new BorderDouble(16, 16, 8, 2);
			}

			// HACK: this is a hack to make the layout engine fire again for this control
			UiThread.RunOnIdle(() => tuningAdjustmentControlsContainer.Width = tuningAdjustmentControlsContainer.Width + 1);

			printer.Connection.CommunicationStateChanged.RegisterEvent(onPrinterStatusChanged, ref unregisterEvents);
			printer.Connection.EnableChanged.RegisterEvent(onPrinterStatusChanged, ref unregisterEvents);

			SetVisibleControls();
		}

		public override void OnClosed(ClosedEventArgs e)
		{
			unregisterEvents?.Invoke(this, null);
			base.OnClosed(e);
		}

		private void onPrinterStatusChanged(object sender, EventArgs e)
		{
			SetVisibleControls();
			UiThread.RunOnIdle(this.Invalidate);
		}

		private void SetVisibleControls()
		{
			if (!printer.Settings.PrinterSelected)
			{
				movementControlsContainer?.SetEnabled(false);
				fanControlsContainer?.SetEnabled(false);
				macroControlsContainer?.SetEnabled(false);
				calibrationControlsContainer?.SetEnabled(false);
				tuningAdjustmentControlsContainer?.SetEnabled(false);
			}
			else // we at least have a printer selected
			{
				switch (printer.Connection.CommunicationState)
				{
					case CommunicationStates.Disconnecting:
					case CommunicationStates.ConnectionLost:
					case CommunicationStates.Disconnected:
					case CommunicationStates.AttemptingToConnect:
					case CommunicationStates.FailedToConnect:
						movementControlsContainer?.SetEnabled(false);
						fanControlsContainer?.SetEnabled(false);
						macroControlsContainer?.SetEnabled(false);
						tuningAdjustmentControlsContainer?.SetEnabled(false);
						calibrationControlsContainer?.SetEnabled(false);

						foreach (var widget in movementControlsContainer.DisableableWidgets)
						{
							widget?.SetEnabled(true);
						}
						movementControlsContainer?.jogControls.SetEnabledLevels(false, false);

						break;

					case CommunicationStates.FinishedPrint:
					case CommunicationStates.Connected:
						movementControlsContainer?.SetEnabled(true);
						fanControlsContainer?.SetEnabled(true);
						macroControlsContainer?.SetEnabled(true);
						tuningAdjustmentControlsContainer?.SetEnabled(true);
						calibrationControlsContainer?.SetEnabled(true);

						foreach (var widget in movementControlsContainer.DisableableWidgets)
						{
							widget?.SetEnabled(true);
						}
						movementControlsContainer?.jogControls.SetEnabledLevels(false, true);
						break;

					case CommunicationStates.PrintingFromSd:
						movementControlsContainer?.SetEnabled(false);
						fanControlsContainer?.SetEnabled(true);
						macroControlsContainer?.SetEnabled(false);
						tuningAdjustmentControlsContainer?.SetEnabled(false);
						calibrationControlsContainer?.SetEnabled(false);
						break;

					case CommunicationStates.PreparingToPrint:
					case CommunicationStates.Printing:
						switch (printer.Connection.DetailedPrintingState)
						{
							case DetailedPrintingState.HomingAxis:
							case DetailedPrintingState.HeatingBed:
							case DetailedPrintingState.HeatingExtruder:
							case DetailedPrintingState.Printing:
								fanControlsContainer?.SetEnabled(true);
								macroControlsContainer?.SetEnabled(false);
								tuningAdjustmentControlsContainer?.SetEnabled(true);
								calibrationControlsContainer?.SetEnabled(false);

								foreach (var widget in movementControlsContainer.DisableableWidgets)
								{
									widget?.SetEnabled(false);
								}

								movementControlsContainer?.jogControls.SetEnabledLevels(true, false);
								break;

							default:
								throw new NotImplementedException();
						}
						break;

					case CommunicationStates.Paused:
						movementControlsContainer?.SetEnabled(true);
						fanControlsContainer?.SetEnabled(true);
						macroControlsContainer?.SetEnabled(true);
						tuningAdjustmentControlsContainer?.SetEnabled(true);
						calibrationControlsContainer?.SetEnabled(true);

						foreach (var widget in movementControlsContainer.DisableableWidgets)
						{
							widget?.SetEnabled(true);
						}
						movementControlsContainer?.jogControls.SetEnabledLevels(false, true);

						break;

					default:
						throw new NotImplementedException();
				}
			}
		}
	}
}
