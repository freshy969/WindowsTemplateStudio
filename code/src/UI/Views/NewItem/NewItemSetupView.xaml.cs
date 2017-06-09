﻿using Microsoft.Templates.UI.ViewModels.NewItem;
using System.Windows.Controls;

namespace Microsoft.Templates.UI.Views.NewItem
{
    public partial class NewItemSetupView : Page
    {
        public MainViewModel ViewModel { get; }

        public NewItemSetupView()
        {
            ViewModel = MainViewModel.Current;
            DataContext = ViewModel;
            InitializeComponent();           
        }
    }
}
