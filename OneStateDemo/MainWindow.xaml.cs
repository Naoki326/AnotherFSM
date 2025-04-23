using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using OneStateDemo.State;
using StateMachine;

namespace OneStateDemo
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        TestState state = new TestState();

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            state.InitBeforeStart();
            state.Context = new FSMNodeContext();
            await (state as IFSMNode).CreateNewAsync();
            (state as IFSMNode).RaisePause -= Node_RaisePause;
            (state as IFSMNode).RaisePause += Node_RaisePause;
            state.InfoChanged -= State_InfoChanged;
            state.InfoChanged += State_InfoChanged;
            Info.Text = "Initialized";
        }

        private void Node_RaisePause()
        {
            (state as IFSMNode).Context.Pause();
        }

        private void State_InfoChanged()
        {
            Dispatcher.Invoke(() =>
            {
                Info.Text = state.Info;
            });
        }

        private async void Button_Click_1(object sender, RoutedEventArgs e)
        {
            SynchronizationContext.SetSynchronizationContext(null);
            await Task.Yield();

            bool isFinished = await (state as IFSMNode).RunAsync();
            if (isFinished)
            {
                Dispatcher.Invoke(() =>
                {
                    Info.Text = "Finished";
                });
            }
        }

        private async void Button_Click_2(object sender, RoutedEventArgs e)
        {
            (state as IFSMNode).Context.Pause();
            try
            {
                await (state as IFSMNode).WaitCurrentTask;
            }
            catch (OperationCanceledException) { }
            Info.Text += " Paused";
        }

        private async void Button_Click_3(object sender, RoutedEventArgs e)
        {
            state.ImitateFailed();
            try
            {
                await(state as IFSMNode).WaitCurrentTask;
            }
            catch (OperationCanceledException) { }
            Dispatcher.Invoke(() =>
            {
                Info.Text += " Interrupted";
            });
        }

        private void Button_Click_4(object sender, RoutedEventArgs e)
        {
            state.DisableFailed();
        }
    }
}