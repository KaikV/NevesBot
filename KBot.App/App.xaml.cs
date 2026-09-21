using System.Windows;
using System;
using KBot.App.Models;
using KBot.App.Services;
using KBot.App.Views;

namespace KBot.App
{
    public partial class App : Application
    {
        private KBotLifecycle? _lifecycle;
        private BootstrapWindow? _bootstrap;
        private MainWindow? _fullWindow;
        private bool _closingForState;
        private int _hiddenTicks;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _lifecycle = new KBotLifecycle();
            _bootstrap = new BootstrapWindow(_lifecycle);
            _bootstrap.Closed += (_, _) => Shutdown();
            _lifecycle.Changed += OnLifecycleChanged;
            MainWindow = _bootstrap;
            _bootstrap.Show();
            await _lifecycle.RestartAsync();
        }

        private void OnLifecycleChanged(KBotLifecycle lifecycle)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_bootstrap is null) return;
                if (lifecycle.CanShowMainWindow)
                {
                    _hiddenTicks = 0;
                    if (_fullWindow is not null) return;
                    _fullWindow = new MainWindow(lifecycle);
                    var opened = _fullWindow;
                    opened.Closed += (_, _) =>
                    {
                        if (ReferenceEquals(_fullWindow, opened)) _fullWindow = null;
                        if (!_closingForState) Shutdown();
                    };
                    MainWindow = opened;
                    _bootstrap.Hide();
                    opened.Show();
                }
                else
                {
                    // A single vision miss must not boot us out of the main window;
                    // require 5 consecutive ticks (~5s) of non-InGame to go back.
                    if (++_hiddenTicks < 5) return;
                    if (_fullWindow is not null)
                    {
                        _closingForState = true;
                        var opened = _fullWindow;
                        _fullWindow = null;
                        opened.Close();
                        _closingForState = false;
                    }
                    MainWindow = _bootstrap;
                    if (!_bootstrap.IsVisible) _bootstrap.Show();
                }
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _lifecycle?.Dispose();
            base.OnExit(e);
        }
    }
}
