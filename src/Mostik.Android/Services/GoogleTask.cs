using Android.Gms.Tasks;
using GTask = Android.Gms.Tasks.Task;

namespace Mostik.Mobile.Services;

internal static class GoogleTask
{
    public static async System.Threading.Tasks.Task<Java.Lang.Object?> AwaitAsync(
        GTask task, System.Threading.CancellationToken token)
    {
        var listener = new Completion();
        task.AddOnCompleteListener(listener);
        // Cancellation stops our wait, not the SDK's already-issued download. See PRIVACY.md.
        return await listener.Source.Task.WaitAsync(token).ConfigureAwait(false);
    }

    private sealed class Completion : Java.Lang.Object, IOnCompleteListener
    {
        internal readonly TaskCompletionSource<Java.Lang.Object?> Source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void OnComplete(GTask task)
        {
            if (task.IsCanceled) Source.TrySetCanceled();
            else if (!task.IsSuccessful) Source.TrySetException(new InvalidOperationException(
                "Операция ML Kit не выполнена. Проверьте пакеты; для первоначальной загрузки нужна сеть.", task.Exception));
            else Source.TrySetResult(task.Result);
        }
    }
}
