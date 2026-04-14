using System;
using System.Linq;
using System.Net.Sockets;

class Program {
    static void Main() {
        var ex = new AggregateException(new Exception("outer", new AggregateException(new SocketException(995))));

        bool IsOperationAborted(Exception e)
        {
            if (e is SocketException se && se.SocketErrorCode == SocketError.OperationAborted)
                return true;
            if (e is AggregateException ae)
            {
                foreach (var inner in ae.Flatten().InnerExceptions)
                {
                    if (IsOperationAborted(inner))
                        return true;
                }
            }
            if (e.InnerException != null)
                return IsOperationAborted(e.InnerException);
            return false;
        }

        Console.WriteLine("Is expected? " + IsOperationAborted(ex));

        var ex2 = new AggregateException(new SocketException(995));
        Console.WriteLine("Is expected2? " + IsOperationAborted(ex2));

        var ex3 = new SocketException(995);
        Console.WriteLine("Is expected3? " + IsOperationAborted(ex3));
    }
}
