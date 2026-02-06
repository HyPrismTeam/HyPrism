using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Collections;
using System;

namespace HyPrism.UI.Components.Inputs.MultiSelectionInput;

public partial class MultiSelectionInput : UserControl
{
    #region Existing Properties
    
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<MultiSelectionInput, string>(nameof(Label));

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public static readonly StyledProperty<IEnumerable> ItemsSourceProperty =
        AvaloniaProperty.Register<MultiSelectionInput, IEnumerable>(nameof(ItemsSource));

    public IEnumerable ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly StyledProperty<string> PlaceholderProperty =
        AvaloniaProperty.Register<MultiSelectionInput, string>(nameof(Placeholder));

    public string Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public static readonly StyledProperty<object?> PlaceholderContentProperty =
        AvaloniaProperty.Register<MultiSelectionInput, object?>(nameof(PlaceholderContent));

    public object? PlaceholderContent
    {
        get => GetValue(PlaceholderContentProperty);
        set => SetValue(PlaceholderContentProperty, value);
    }

    public static readonly StyledProperty<IDataTemplate?> PlaceholderTemplateProperty =
        AvaloniaProperty.Register<MultiSelectionInput, IDataTemplate?>(nameof(PlaceholderTemplate));

    public IDataTemplate? PlaceholderTemplate
    {
        get => GetValue(PlaceholderTemplateProperty);
        set => SetValue(PlaceholderTemplateProperty, value);
    }

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<MultiSelectionInput, object?>(nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public static readonly StyledProperty<object?> ListSelectedItemProperty =
        AvaloniaProperty.Register<MultiSelectionInput, object?>(nameof(ListSelectedItem), defaultBindingMode: BindingMode.TwoWay);

    public object? ListSelectedItem
    {
        get => GetValue(ListSelectedItemProperty);
        set => SetValue(ListSelectedItemProperty, value);
    }

    public static readonly StyledProperty<IList?> SelectedItemsProperty =
        AvaloniaProperty.Register<MultiSelectionInput, IList?>(nameof(SelectedItems), defaultBindingMode: BindingMode.TwoWay);

    public IList? SelectedItems
    {
        get => GetValue(SelectedItemsProperty);
        set => SetValue(SelectedItemsProperty, value);
    }

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<MultiSelectionInput, IDataTemplate?>(nameof(ItemTemplate));

    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public static readonly StyledProperty<IDataTemplate?> ItemsTemplateProperty =
        AvaloniaProperty.Register<MultiSelectionInput, IDataTemplate?>(nameof(ItemsTemplate));

    public IDataTemplate? ItemsTemplate
    {
        get => GetValue(ItemsTemplateProperty);
        set => SetValue(ItemsTemplateProperty, value);
    }

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<MultiSelectionInput, object?>(nameof(HeaderContent));

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    public static readonly StyledProperty<IDataTemplate?> HeaderTemplateProperty =
        AvaloniaProperty.Register<MultiSelectionInput, IDataTemplate?>(nameof(HeaderTemplate));

    public IDataTemplate? HeaderTemplate
    {
        get => GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    public static readonly StyledProperty<SelectionMode> SelectionModeProperty =
        AvaloniaProperty.Register<MultiSelectionInput, SelectionMode>(nameof(SelectionMode), SelectionMode.Multiple);

    public SelectionMode SelectionMode
    {
        get => GetValue(SelectionModeProperty);
        set => SetValue(SelectionModeProperty, value);
    }

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<MultiSelectionInput, bool>(nameof(IsExpanded));

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    #endregion

    #region New Customization Properties
    
    /// <summary>
    /// Height of the toggle button. Default: 52
    /// </summary>
    public static readonly StyledProperty<double> ToggleHeightProperty =
        AvaloniaProperty.Register<MultiSelectionInput, double>(nameof(ToggleHeight), 52.0);

    public double ToggleHeight
    {
        get => GetValue(ToggleHeightProperty);
        set => SetValue(ToggleHeightProperty, value);
    }

    /// <summary>
    /// Background color of the dropdown panel.
    /// </summary>
    public static readonly StyledProperty<IBrush?> DropdownBackgroundProperty =
        AvaloniaProperty.Register<MultiSelectionInput, IBrush?>(nameof(DropdownBackground));

    public IBrush? DropdownBackground
    {
        get => GetValue(DropdownBackgroundProperty);
        set => SetValue(DropdownBackgroundProperty, value);
    }

    /// <summary>
    /// Border brush of the dropdown panel.
    /// </summary>
    public static readonly StyledProperty<IBrush?> DropdownBorderBrushProperty =
        AvaloniaProperty.Register<MultiSelectionInput, IBrush?>(nameof(DropdownBorderBrush));

    public IBrush? DropdownBorderBrush
    {
        get => GetValue(DropdownBorderBrushProperty);
        set => SetValue(DropdownBorderBrushProperty, value);
    }

    /// <summary>
    /// Maximum height of the dropdown panel. Default: 320
    /// </summary>
    public static readonly StyledProperty<double> MaxDropdownHeightProperty =
        AvaloniaProperty.Register<MultiSelectionInput, double>(nameof(MaxDropdownHeight), 320.0);

    public double MaxDropdownHeight
    {
        get => GetValue(MaxDropdownHeightProperty);
        set => SetValue(MaxDropdownHeightProperty, value);
    }

    /// <summary>
    /// Width of the dropdown panel. If not set, uses Auto.
    /// </summary>
    public static readonly StyledProperty<double> DropdownWidthProperty =
        AvaloniaProperty.Register<MultiSelectionInput, double>(nameof(DropdownWidth), double.NaN);

    public double DropdownWidth
    {
        get => GetValue(DropdownWidthProperty);
        set => SetValue(DropdownWidthProperty, value);
    }

    /// <summary>
    /// Corner radius of the toggle button.
    /// </summary>
    public static readonly StyledProperty<CornerRadius> ToggleCornerRadiusProperty =
        AvaloniaProperty.Register<MultiSelectionInput, CornerRadius>(nameof(ToggleCornerRadius), new CornerRadius(8));

    public CornerRadius ToggleCornerRadius
    {
        get => GetValue(ToggleCornerRadiusProperty);
        set => SetValue(ToggleCornerRadiusProperty, value);
    }

    /// <summary>
    /// Corner radius of the dropdown panel.
    /// </summary>
    public static readonly StyledProperty<CornerRadius> DropdownCornerRadiusProperty =
        AvaloniaProperty.Register<MultiSelectionInput, CornerRadius>(nameof(DropdownCornerRadius), new CornerRadius(12));

    public CornerRadius DropdownCornerRadius
    {
        get => GetValue(DropdownCornerRadiusProperty);
        set => SetValue(DropdownCornerRadiusProperty, value);
    }

    #endregion

    private bool _updatingSelection;
    private IDisposable? _pointerOutsideHandler;
    private ListBox? _itemsListBox;

    public MultiSelectionInput()
    {
        InitializeComponent();
        _itemsListBox = this.FindControl<ListBox>("ItemsListBox");
    }

    private void OnListBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox || _updatingSelection)
        {
            return;
        }

        if (listBox.SelectedItem == null)
        {
            return;
        }

        var isUserSelection = listBox.IsPointerOver || listBox.IsFocused;
        if (!isUserSelection)
        {
            return;
        }

        _updatingSelection = true;
        ListSelectedItem = listBox.SelectedItem;
        _updatingSelection = false;

        if (SelectionMode == SelectionMode.Single)
        {
            IsExpanded = false;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PlaceholderProperty)
        {
            var oldValue = change.GetOldValue<string>();
            var newValue = change.GetNewValue<string>();

            if (PlaceholderContent is null || (PlaceholderContent is string s && s == oldValue))
            {
                PlaceholderContent = newValue;
            }
        }

        if (change.Property == IsExpandedProperty)
        {
            var expanded = change.GetNewValue<bool>();

            int zIndex = expanded ? 999 : 0;
            SetValue(Panel.ZIndexProperty, zIndex);

            if (Parent is Panel parentPanel)
            {
                parentPanel.SetValue(Panel.ZIndexProperty, zIndex);
            }

            UpdateOutsideClickHandlers(expanded);
        }

        if (change.Property == ItemsSourceProperty)
        {
            _updatingSelection = true;
            ListSelectedItem = null;
            if (_itemsListBox != null)
            {
                _itemsListBox.SelectedItem = null;
            }
            _updatingSelection = false;
        }
    }

    private void UpdateOutsideClickHandlers(bool enable)
    {
        if (enable)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                _pointerOutsideHandler = topLevel.AddDisposableHandler(
                    InputElement.PointerPressedEvent,
                    OnOutsidePointerPressed,
                    RoutingStrategies.Tunnel);
            }
        }
        else
        {
            _pointerOutsideHandler?.Dispose();
            _pointerOutsideHandler = null;
        }
    }

    private void OnOutsidePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsExpanded) return;

        if (e.Source is Visual source)
        {
            if (!this.IsVisualAncestorOf(source))
            {
                IsExpanded = false;
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
