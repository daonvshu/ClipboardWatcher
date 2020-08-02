using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClipboardWatcher {
    public delegate void Runnable();

    public class MainContextRunner
    {
        private static MainContextRunner _instance = null;

        private MainContextRunner() { }

        private static MainContextRunner GetInstance() {
            if (_instance == null) {
                _instance = new MainContextRunner();
            }
            return _instance;
        }

        private SynchronizationContext mainContext = null;
        private int mainContextThreadId = -1;

        public static void AttachMainContext(SynchronizationContext context) {
            GetInstance().mainContext = context;
            GetInstance().mainContextThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public static void Post(Runnable runnable) {
            if (Thread.CurrentThread.ManagedThreadId == GetInstance().mainContextThreadId) {
                runnable();
            } else {
                GetInstance().mainContext.Post((object state) => {
                    runnable();
                }, 0);
            }
        }
    }
}
