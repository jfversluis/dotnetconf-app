#if IOS_DUO_BINDINGS
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DotNetConf.Foldable.iOS.Bindings;
using Foundation;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using ObjCRuntime;
using UIKit;

namespace Microsoft.Maui.Foldable
{
	class FoldableService : IFoldableServiceCompat, IDisposable
	{
		readonly WeakEventManager _hingeAngleChangedEventManager = new WeakEventManager();
		readonly WeakEventManager _onLayoutChangedEventManager = new WeakEventManager();
		readonly WeakEventManager _onScreenChangedEventManager = new WeakEventManager();
		readonly FoldableMonitorRegistry<VisualElement, ViewMonitor> _monitors;
		readonly FoldableHingeAngleState _hingeAngleState = new FoldableHingeAngleState();
		UIWindow _window;
		ViewMonitor _windowMonitor;

		public FoldableService()
		{
			_monitors = new FoldableMonitorRegistry<VisualElement, ViewMonitor>(DisposeMonitor);
			DeviceDisplay.MainDisplayInfoChanged += OnDisplayInfoChanged;
		}

		public event EventHandler OnScreenChanged
		{
			add => _onScreenChangedEventManager.AddEventHandler(value);
			remove => _onScreenChangedEventManager.RemoveEventHandler(value);
		}

		public event EventHandler<FoldEventArgs> OnLayoutChanged
		{
			add => _onLayoutChangedEventManager.AddEventHandler(value);
			remove => _onLayoutChangedEventManager.RemoveEventHandler(value);
		}

		public event EventHandler<FoldableHingeAngleChangedEventArgs> HingeAngleChanged
		{
			add => _hingeAngleChangedEventManager.AddEventHandler(value);
			remove => _hingeAngleChangedEventManager.RemoveEventHandler(value);
		}

		public bool IsSpanned => false;

		public bool IsLandscape => DeviceDisplay.MainDisplayInfo.Orientation.IsLandscape();

		public Size ScaledScreenSize => DeviceDisplay.MainDisplayInfo.GetScaledScreenSize();

		public Task<int> GetHingeAngleAsync()
		{
			if (!OperatingSystem.IsIOSVersionAtLeast(27, 1))
			{
				_hingeAngleState.SetAngle(0);
				return Task.FromResult(0);
			}

			var angleTask = _hingeAngleState.GetAngleAsync();
			MainThread.BeginInvokeOnMainThread(RefreshHingeState);
			return angleTask;
		}

		public Rect GetHinge() => Rect.Zero;

		public Rect GetHinge(VisualElement visualElement)
		{
			var platformView = GetPlatformView(visualElement);
			if (platformView?.Window == null || !OperatingSystem.IsIOSVersionAtLeast(27, 1))
				return Rect.Zero;

			using var kind = SendClassMessage("UIViewReservedRegionKind", "divisionRegionKind", owns: false);
			var regions = platformView.GetReservedRegions(kind);
			if (regions == null || regions.Length == 0)
				return Rect.Zero;

			var candidates = new List<FoldableRegion>(regions.Length);
			for (int i = 0; i < regions.Length; i++)
			{
				var region = regions[i];
				var frame = region.GetReservedRegionFrame();
				candidates.Add(new FoldableRegion(
					new Rect(frame.X, frame.Y, frame.Width, frame.Height),
					region.IsReservedRegionActive()));
			}

			var localRegion = FoldableRegionHelper.GetActiveDivisionRegion(
				candidates,
				new Rect(0, 0, platformView.Bounds.Width, platformView.Bounds.Height));

			if (localRegion == Rect.Zero)
				return Rect.Zero;

			var windowRegion = platformView.ConvertRectToView(
				new CoreGraphics.CGRect(localRegion.X, localRegion.Y, localRegion.Width, localRegion.Height),
				platformView.Window);

			return new Rect(windowRegion.X, windowRegion.Y, windowRegion.Width, windowRegion.Height);
		}

		public bool IsLandscapeFor(VisualElement visualElement)
		{
			var window = GetPlatformView(visualElement)?.Window ?? _window;
			if (window == null)
				return IsLandscape;

			return window.Bounds.Width >= window.Bounds.Height;
		}

		public Size GetScaledScreenSize(VisualElement visualElement)
		{
			var window = GetPlatformView(visualElement)?.Window ?? _window;
			if (window == null)
				return ScaledScreenSize;

			return new Size(window.Bounds.Width, window.Bounds.Height);
		}

		public Point? GetLocationOnScreen(VisualElement visualElement)
		{
			var platformView = GetPlatformView(visualElement);
			if (platformView?.Window == null)
				return null;

			var frame = platformView.ConvertRectToView(platformView.Bounds, platformView.Window);
			return new Point(frame.X, frame.Y);
		}

		public bool StartMonitoring(VisualElement visualElement)
		{
			if (!OperatingSystem.IsIOSVersionAtLeast(27, 1))
			{
				_hingeAngleState.SetAngle(0);
				return true;
			}

			if (GetPlatformView(visualElement) is not UIView platformView ||
				platformView.Window == null)
			{
				return false;
			}

			if (visualElement == null)
			{
				var interactionView = platformView.Window.RootViewController?.View ?? platformView.Window;
				if (ReferenceEquals(_windowMonitor?.View, interactionView))
					return true;

				if (_windowMonitor != null)
					DisposeMonitor(_windowMonitor);

				_windowMonitor = CreateMonitor(interactionView);
				return true;
			}

			_monitors.GetOrAdd(visualElement, () =>
			{
				var interactionView = platformView.Window.RootViewController?.View ?? platformView.Window;
				return CreateMonitor(interactionView);
			}, monitor =>
			{
				var interactionView = platformView.Window.RootViewController?.View ?? platformView.Window;
				return !ReferenceEquals(monitor.View, interactionView);
			});
			return true;
		}

		public void StopMonitoring(VisualElement visualElement)
		{
			if (visualElement == null)
			{
				if (_windowMonitor != null)
					DisposeMonitor(_windowMonitor);

				_windowMonitor = null;
				return;
			}

			_monitors.Remove(visualElement);
		}

		public void Dispose()
		{
			DeviceDisplay.MainDisplayInfoChanged -= OnDisplayInfoChanged;
			_monitors.Dispose();

			if (_windowMonitor != null)
				DisposeMonitor(_windowMonitor);

			_windowMonitor = null;
			_window = null;
		}

		internal void SetWindow(UIWindow window)
		{
			_window = window;
		}

		UIView GetPlatformView(VisualElement visualElement)
		{
			return visualElement?.Handler?.PlatformView as UIView ??
				_window?.RootViewController?.View;
		}

		ViewMonitor CreateMonitor(UIView interactionView)
		{
			if (!OperatingSystem.IsIOSVersionAtLeast(27, 1))
				throw new PlatformNotSupportedException("Hinge monitoring requires iOS 27.1 or later.");

			UIHingeInteractionUpdateHandler updateHandler = OnHingeUpdated;
			var allocated = SendClassMessage("UIHingeInteraction", "alloc", owns: true);
			var interaction = allocated.InitializeHingeInteraction(updateHandler);
			if (interaction.Handle != allocated.Handle)
				throw new InvalidOperationException("The hinge interaction initializer returned a different object.");

			interactionView.AddHingeInteraction(interaction);
			return new ViewMonitor(interactionView, interaction, updateHandler);
		}

		static NSObject SendClassMessage(string className, string selector, bool owns)
		{
			var handle = SendObjectMessage(Class.GetHandle(className), Selector.GetHandle(selector));
			return Runtime.GetNSObject<NSObject>(handle, owns)
				?? throw new InvalidOperationException($"UIKit did not return an object for {className}.{selector}.");
		}

		[DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
		static extern IntPtr SendObjectMessage(IntPtr receiver, IntPtr selector);

		void RefreshHingeState()
		{
			if (_windowMonitor != null)
				RefreshMonitor(_windowMonitor);

			_monitors.ForEach(RefreshMonitor);
		}

		static void RefreshMonitor(ViewMonitor monitor)
		{
			if (!OperatingSystem.IsIOSVersionAtLeast(27, 1))
				return;

			monitor.Interaction.SetHingeInteractionEnabled(false);
			monitor.Interaction.SetHingeInteractionEnabled(true);
		}

		void OnHingeUpdated(NSObject interaction, NSObject update)
		{
			if (!OperatingSystem.IsIOSVersionAtLeast(27, 1))
				return;

			var hinge = update.GetHinge();
			var newAngle = hinge == null ? 0d : FoldableRegionHelper.RadiansToDegrees((double)hinge.GetHingeAngle());
			if (_hingeAngleState.SetAngle(newAngle))
			{
				_hingeAngleChangedEventManager.HandleEvent(
					this,
					new FoldableHingeAngleChangedEventArgs(newAngle),
					nameof(HingeAngleChanged));
			}

			RaiseLayoutChanged();
		}

		void OnDisplayInfoChanged(object sender, DisplayInfoChangedEventArgs e)
		{
			_onScreenChangedEventManager.HandleEvent(this, e, nameof(OnScreenChanged));
			RaiseLayoutChanged();
		}

		void RaiseLayoutChanged()
		{
			_onLayoutChangedEventManager.HandleEvent(
				this,
				new FoldEventArgs(),
				nameof(OnLayoutChanged));
		}

		static void DisposeMonitor(ViewMonitor monitor)
		{
			if (!OperatingSystem.IsIOSVersionAtLeast(27, 1))
				return;

			monitor.Interaction.SetHingeInteractionEnabled(false);
			monitor.View.RemoveHingeInteraction(monitor.Interaction);
			monitor.Interaction.Dispose();
		}

		sealed class ViewMonitor
		{
			public ViewMonitor(
				UIView view,
				NSObject interaction,
				UIHingeInteractionUpdateHandler updateHandler)
			{
				View = view;
				Interaction = interaction;
				UpdateHandler = updateHandler;
			}

			public UIView View { get; }
			public NSObject Interaction { get; }
			public UIHingeInteractionUpdateHandler UpdateHandler { get; }
		}
	}
}
#endif
