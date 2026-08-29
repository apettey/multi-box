using System.Windows;
using System.Windows.Input;
using MultiBox.App.ViewModels;
using MultiBox.Core.Config;

namespace MultiBox.App;

/// <summary>
/// Reorders the whole fleet, for when there are more characters than dragging cards around
/// the grid can comfortably arrange.
/// </summary>
public partial class CharacterOrderWindow : Window
{
    private readonly CharacterOrderViewModel _viewModel;

    public CharacterOrderWindow(MultiBoxConfig config, IEnumerable<(string Name, bool Running)> characters)
    {
        InitializeComponent();
        _viewModel = new CharacterOrderViewModel(config, characters);
        DataContext = _viewModel;
    }

    /// <summary>The order chosen, top to bottom.</summary>
    public IReadOnlyList<string> Order => _viewModel.Order;

    private void MoveUp_Click(object sender, MouseButtonEventArgs e) => Move(-1);

    private void MoveDown_Click(object sender, MouseButtonEventArgs e) => Move(1);

    private void Move(int delta)
    {
        _viewModel.Move(delta);

        // The list keeps the moved row selected; scrolling to it means a run of presses does
        // not walk a character off the top of the view.
        if (_viewModel.Selected is not null)
            OrderList.ScrollIntoView(_viewModel.Selected);
    }

    private void RunningFirst_Click(object sender, MouseButtonEventArgs e) => _viewModel.RunningFirst();

    private void Close_Click(object sender, MouseButtonEventArgs e) => Close();
}
