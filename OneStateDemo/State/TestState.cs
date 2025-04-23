using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StateMachine;

namespace OneStateDemo.State
{
    internal class TestState : AsyncEnumFSMNode
    {

        private bool isFailed = false;
        public void ImitateFailed()
        {
            isFailed = true;
        }
        public void DisableFailed()
        {
            isFailed = false;
        }

        public event Action InfoChanged;

        private string info = "";
        public string Info { get => info; set { info = value; InfoChanged?.Invoke(); } }

        private void EachStep(int stepNum)
        {
            try
            {
                Info = $"Step {stepNum} Running";
                Task.Delay(2000).Wait(Context.Token);
                Info = $"Step {stepNum} Exit";
                Task.Delay(2000).Wait(Context.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void EachStepInterruptDisable(int stepNum)
        {
            Info = $"Step {stepNum} Running";
            Thread.Sleep(2000);
            Info = $"Step {stepNum} Exit";
            Thread.Sleep(2000);
        }

        private async Task Restore(int stepNum)
        {
            Info = $"Step Restore {stepNum} ";
            await Task.Delay(2000, Context.Token);
            Info = $"Step Restore {stepNum}  Exit";
            await Task.Delay(2000, Context.Token);
        }

        private async Task TestFailed(int stepNum)
        {
            Stopwatch sw = new Stopwatch();
            sw.Restart();
            Info = $"Step RetryIfFailed {stepNum}";
            while (sw.ElapsedMilliseconds < 2000)
            {
                if (isFailed)
                {
                    Info = $"throw exception";
                    throw new Exception();
                }
                await Task.Delay(10, Context.Token);
            }
            Info = $"Step RetryIfFailed {stepNum} Exit";
            while (sw.ElapsedMilliseconds < 4000)
            {
                if (isFailed)
                {
                    Info = $"throw exception";
                    throw new Exception();
                }
                await Task.Delay(10, Context.Token);
            }
        }

        protected override async IAsyncEnumerable<object> ExecuteEnumerable()
        {
            EachStepInterruptDisable(1);

            yield return Yield.None;

            EachStepInterruptDisable(2);

            yield return Yield.None;

            EachStepInterruptDisable(3);

            yield return Yield.RetryIfFailed(() =>
            {
                return TestFailed(333);
            });


            EachStepInterruptDisable(4);

            yield return Yield.None;

            EachStepInterruptDisable(5);

            yield return Yield.None;

            EachStepInterruptDisable(6);

            yield return Yield.PauseIfFailed(() =>
            {
                return TestFailed(333);
            });



            EachStep(7);

            yield return Yield.RestoreIfPause(() =>
            {
                return Restore(7);
            });

            EachStep(8);

            yield return Yield.RestoreIfPause(() =>
            {
                return Restore(8);
            });

            EachStep(9);

            yield return Yield.RestoreIfPause(() =>
            {
                return Restore(9);
            });

            yield break;
        }
    }
}
