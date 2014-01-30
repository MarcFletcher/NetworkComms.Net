Imports NetworkCommsDotNet

Module BasicSend
    Sub RunExample()
        'We need to define what happens when packets are received.
        'To do this we add an incoming packet handler for a 'Message' packet type. 
        '
        'We will define what we want the handler to do inline by using a lambda expression
        'http:'msdn.microsoft.com/en-us/library/bb397687.aspx.
        'We could also just point the AppendGlobalIncomingPacketHandler method 
        'to a standard method (See AdvancedSend example)
        '
        'This handler will convert the incoming raw bytes into a string (this is what 
        'the <string> bit means) and then write that string to the local console window.
        NetworkComms.AppendGlobalIncomingPacketHandler(Of String)("Message", Sub(packetHeader As PacketHeader, connection As Connection, incomingString As String) Console.WriteLine(Environment.NewLine + "  ... Incoming message from " + connection.ToString() + " saying '" + incomingString + "'."))

        'Start listening for incoming 'TCP' connections.
        'We want to select a random port on all available adaptors so provide 
        'an IPEndPoint using IPAddress.Any and port 0.
        'See also Connection.StartListening(ConnectionType.UDP, IPEndPoint)
        Connection.StartListening(ConnectionType.TCP, New IPEndPoint(IPAddress.Any, 0))

        'Print the IP addresses and ports we are listening on to make sure everything
        'worked as expected.
        Console.WriteLine("Listening for TCP messages on:")
        For Each localEndPoint As System.Net.IPEndPoint In Connection.ExistingLocalListenEndPoints(ConnectionType.TCP)
            Console.WriteLine("{0}:{1}", localEndPoint.Address, localEndPoint.Port)
        Next

        'We loop here to allow any number of test messages to be sent and received
        While True
            'Request a message to send somewhere
            Console.WriteLine(Environment.NewLine + "Please enter your message and press enter (Type 'exit' to quit):")
            Dim stringToSend As String = Console.ReadLine()

            'If the user has typed exit then we leave our loop and end the example
            If stringToSend = "exit" Then
                Exit While
            Else
                'Once we have a message we need to know where to send it
                'We have created a small wrapper class to help keep things clean here
                Dim targetServerConnectionInfo As ConnectionInfo = ExampleHelper.GetServerDetails()

                'There are loads of ways of sending data (see AdvancedSend example for more)
                'but the most simple, which we use here, just uses an IP address (string) and port (integer) 
                'We pull these values out of the ConnectionInfo object we got above and voila!
                NetworkComms.SendObject("Message",
                                        CType(targetServerConnectionInfo.RemoteEndPoint, IPEndPoint).Address.ToString(),
                                        CType(targetServerConnectionInfo.RemoteEndPoint, IPEndPoint).Port,
                                        stringToSend)
            End If
        End While

        'We should always call shutdown on comms if we have used it
        NetworkComms.Shutdown()

    End Sub
End Module
