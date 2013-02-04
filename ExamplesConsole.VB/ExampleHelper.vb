Imports NetworkCommsDotNet

Module ExampleHelper
    Private lastServerIPEndPoint As IPEndPoint = Nothing

    Function GetServerDetails() As ConnectionInfo
        If Not IsNothing(lastServerIPEndPoint) Then
            Console.WriteLine("Please enter the destination IP and port. To reuse '{0}:{1}' use r:", lastServerIPEndPoint.Address, lastServerIPEndPoint.Port)
        Else
            Console.WriteLine("Please enter the destination IP address and port, e.g. '192.168.0.1:10000':")
        End If

        While True
            Try
                'Parse the provided information
                Dim userEnteredStr As String = Console.ReadLine()

                If userEnteredStr.Trim() = "r" And Not IsNothing(lastServerIPEndPoint) Then
                    Exit While
                Else
                    lastServerIPEndPoint = IPTools.ParseEndPointFromString(userEnteredStr)
                    Exit While
                End If
            Catch ex As Exception
                Console.WriteLine("Unable to determine host IP address and port. Check format and try again:")
            End Try
        End While

        Return New ConnectionInfo(lastServerIPEndPoint)
    End Function
End Module
