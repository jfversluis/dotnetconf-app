using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Foldable
{
    internal interface IFoldableServiceCompat
    {
        event EventHandler OnScreenChanged;
        event EventHandler<FoldableHingeAngleChangedEventArgsCompat> HingeAngleChanged;
        bool IsSpanned { get; }
        bool IsLandscape { get; }
        Rect GetHinge();
        Rect GetHinge(VisualElement visualElement);
        bool IsLandscapeFor(VisualElement visualElement);
        Size GetScaledScreenSize(VisualElement visualElement);
        Size ScaledScreenSize { get; }
        Point? GetLocationOnScreen(VisualElement visualElement);
        Task<int> GetHingeAngleAsync();
        bool StartMonitoring(VisualElement visualElement);
        void StopMonitoring(VisualElement visualElement);
        event EventHandler<FoldEventArgs> OnLayoutChanged;
    }

    internal sealed class FoldableHingeAngleChangedEventArgsCompat : EventArgs
    {
        public FoldableHingeAngleChangedEventArgsCompat(double hingeAngleInDegrees) =>
            HingeAngleInDegrees = hingeAngleInDegrees;

        public double HingeAngleInDegrees { get; }
    }
}
