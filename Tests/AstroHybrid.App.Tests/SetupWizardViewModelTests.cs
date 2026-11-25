using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using AstroHybrid.App.Models;
using AstroHybrid.App.Services;
using AstroHybrid.App.ViewModels;
using Xunit;

namespace AstroHybrid.App.Tests
{
    public sealed class SetupWizardViewModelTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "astrohybrid-wizard-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task StaleOrCancelledValidationCannotProceed()
        {
            var pending = new TaskCompletionSource<GameInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            var vm = new SetupWizardViewModel(_root, (path, progress, token) => pending.Task);
            vm.GameDumpPath = Path.Combine(_root, "old.zar");
            var validation = StartValidation(vm);
            vm.GameDumpPath = Path.Combine(_root, "new.zar");
            pending.SetResult(Valid("old.zar"));
            await validation;
            Assert.False(vm.CanProceed);
            Assert.Null(vm.GameInfo);
            Assert.False(vm.IsValidating);
            Assert.True(vm.ValidateCommand.CanExecute(null));

            var cancelled = new TaskCompletionSource<GameInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            vm = new SetupWizardViewModel(_root, (path, progress, token) => cancelled.Task);
            vm.GameDumpPath = Path.Combine(_root, "cancel.zar");
            validation = StartValidation(vm);
            var closeRaised = false;
            vm.SetupCancelled += () => closeRaised = true;
            vm.CancelCommand.Execute(null);
            cancelled.SetResult(Valid("cancel.zar"));
            await validation;
            Assert.True(closeRaised);
            Assert.False(vm.CanProceed);
            Assert.Null(vm.GameInfo);
            Assert.False(vm.IsValidating);
            Assert.True(vm.ValidateCommand.CanExecute(null));
        }

        [Fact]
        public async Task SuccessfulValidationRequestsInstallRequery()
        {
            var vm = new SetupWizardViewModel(_root,
                (path, progress, token) => Task.FromResult(Valid(path)));
            vm.GameDumpPath = Path.Combine(_root, "valid.zar");
            var requeryRequested = false;
            vm.InstallCommand.CanExecuteChanged += (_, _) => requeryRequested = true;

            await StartValidation(vm);

            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Assert.True(vm.CanProceed);
            Assert.True(vm.InstallCommand.CanExecute(null));
            Assert.True(requeryRequested);
        }

        private static Task StartValidation(SetupWizardViewModel vm)
        {
            var method = typeof(SetupWizardViewModel).GetMethod("ValidateGameDumpAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return (Task)method!.Invoke(vm, null)!;
        }

        private static GameInfo Valid(string path) => new GameInfo
        {
            Path = path,
            IsValid = true,
            Version = "01.019.000",
            TitleId = "PPSA21567"
        };

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
