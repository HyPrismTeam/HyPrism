using System;
using Avalonia.Controls;
using Avalonia.Styling;

namespace HyPrism.UI.Components.Buttons.PrimaryButton;

public class PrimaryButton : Button
{
    protected override Type StyleKeyOverride => typeof(PrimaryButton);
}