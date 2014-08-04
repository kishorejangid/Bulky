using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace ContentServerFolderUploader
{
    /// <summary>
    /// A base class for command implementations.
    /// </summary>
    public abstract class Command : ICommand
    {
        private readonly Dispatcher _dispatcher;

        /// <summary>
        /// Constructs an instance of <c>CommandBase</c>.
        /// </summary>
        protected Command()
        {
            if (Application.Current != null)
            {
                _dispatcher = Application.Current.Dispatcher;
            }
            else
            {
                //this is useful for unit tests where there is no application running
                _dispatcher = Dispatcher.CurrentDispatcher;
            }

            Debug.Assert(_dispatcher != null);
        }

        /// <summary>
        /// Occurs whenever the state of the application changes such that the result of a call to <see cref="CanExecute"/> may return a different value.
        /// </summary>
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        /// <summary>
        /// Determines whether this command can execute.
        /// </summary>
        /// <param name="parameter">
        /// The command parameter.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the command can execute, otherwise <see langword="false"/>.
        /// </returns>
        public abstract bool CanExecute(object parameter);

        /// <summary>
        /// Executes this command.
        /// </summary>
        /// <param name="parameter">
        /// The command parameter.
        /// </param>
        public abstract void Execute(object parameter);

        /// <summary>
        /// Raises the <see cref="CanExecuteChanged"/> event.
        /// </summary>
        protected virtual void OnCanExecuteChanged()
        {
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.Invoke((ThreadStart)OnCanExecuteChanged, DispatcherPriority.Normal);
            }
            else
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}