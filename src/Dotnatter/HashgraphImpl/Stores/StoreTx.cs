using System;

namespace Dotnatter.HashgraphImpl.Stores
{
    public class StoreTx:IDisposable
    {
        private readonly Action commitAction;
        private readonly Action disposeAction;

        public StoreTx(Action commitAction, Action disposeAction)
        {
            this.commitAction = commitAction;
            this.disposeAction = disposeAction;
        }
        public void Dispose()
        {
            disposeAction?.Invoke();
        }

        public void Commit()
        {
            commitAction?.Invoke();
        }
    }
}