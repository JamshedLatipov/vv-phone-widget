using System;
using System.Net.Sockets;

class App {
    static void Main() {
        var e = new AggregateException(new Exception("test", new AggregateException(new SocketException(995))));
        var inner = e.InnerException ?? e;
        if (inner is System.Net.Sockets.SocketException se &&
            se.SocketErrorCode == System.Net.Sockets.SocketError.OperationAborted) {
            Console.WriteLine("Matched with current logic");
        } else {
            Console.WriteLine("Did NOT match with current logic");
        }

        bool IsOperationAborted(Exception ex)
        {
            if (ex is SocketException s && s.SocketErrorCode == SocketError.OperationAborted) return true;
            if (ex is AggregateException ae)
            {
                foreach (var i in ae.Flatten().InnerExceptions)
                    if (IsOperationAborted(i)) return true;
            }
            if (ex.InnerException != null) return IsOperationAborted(ex.InnerException);
            return false;
        }

        if (IsOperationAborted(e)) {
            Console.WriteLine("Matched with new logic");
        }
    }
}
