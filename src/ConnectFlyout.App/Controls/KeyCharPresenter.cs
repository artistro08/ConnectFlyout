// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
//
// Ported from PowerToys: src/common/Common.UI.Controls/Controls/KeyVisual/KeyCharPresenter.xaml.cs
// (https://github.com/microsoft/PowerToys), by way of Leaf Calendar.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectFlyout.App.Controls;

/// <summary>What's printed on a <see cref="KeyVisual"/>: text, a glyph, or the Windows logo, by its style.</summary>
public sealed partial class KeyCharPresenter : Control
{
    /// <summary>Creates the presenter.</summary>
    public KeyCharPresenter()
    {
        DefaultStyleKey = typeof(KeyCharPresenter);
    }

    /// <summary>The text or glyph.</summary>
    public object Content
    {
        get => (object)GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <summary>Identifies <see cref="Content"/>.</summary>
    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(nameof(Content), typeof(object), typeof(KeyCharPresenter), new PropertyMetadata(default(string)));
}
