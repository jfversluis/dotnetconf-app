using CoreGraphics;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace DotNetConf.Foldable.iOS.Bindings;

[iOS(27, 1)]
delegate void UIHingeInteractionUpdateHandler(NSObject interaction, NSObject update);

// Binding UIKit classes directly makes the NativeAOT registrar redeclare system interfaces.
[iOS(27, 1)]
[Category]
[BaseType(typeof(NSObject))]
interface FoldableObjectExtensions
{
    [Export("initWithUpdateHandler:")]
    NSObject InitializeHingeInteraction(UIHingeInteractionUpdateHandler updateHandler);

    [Export("setEnabled:")]
    void SetHingeInteractionEnabled(bool enabled);

    [return: NullAllowed]
    [Export("hinge")]
    NSObject GetHinge();

    [Export("angle")]
    nfloat GetHingeAngle();

    [Export("frame")]
    CGRect GetReservedRegionFrame();

    [Export("isActive")]
    bool IsReservedRegionActive();
}

[iOS(27, 1)]
[Category]
[BaseType(typeof(UIView))]
interface FoldableViewExtensions
{
    [Export("reservedRegionsOfKind:")]
    NSObject[] GetReservedRegions(NSObject kind);

    [Export("addInteraction:")]
    void AddHingeInteraction(NSObject interaction);

    [Export("removeInteraction:")]
    void RemoveHingeInteraction(NSObject interaction);
}
