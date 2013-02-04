Imports NetworkCommsDotNet

Module Program

    Sub Main()
        Try
            Console.SetBufferSize(120, 200)
            Console.SetWindowSize(120, 25)
        Catch ex As NotImplementedException
        End Try

        Thread.CurrentThread.Name = "MainThread"

        Console.WriteLine("Initiating NetworkCommsDotNet examples." + Environment.NewLine)

        'All we do here is let the user choice a specific example
        Console.WriteLine("Please enter an example number:")

        'Print out the available examples
        Dim totalNumberOfExamples As Integer = 1
        Console.WriteLine("1 - Basic - Message Send (Only 11 lines!)")

        'Get the user choice
        Console.WriteLine("")
        Dim selectedExample As Integer
        While True
            Dim parseSucces As Boolean = Integer.TryParse(Console.ReadKey().KeyChar.ToString(), selectedExample)
            If parseSucces And selectedExample <= totalNumberOfExamples Then Exit While
            Console.WriteLine(Environment.NewLine + "Invalid example choice. Please try again.")
        End While

        'Clear all input so that each example can do it's own thing
        Console.Clear()

        'Run the selected example
        Try
            Select Case selectedExample
                Case 1
                    BasicSend.RunExample()
                Case Else
                    Console.WriteLine("Selected an invalid example number. Please restart and try again.")
            End Select
        Catch ex As Exception
            NetworkComms.LogError(ex, "ExampleError")
            NetworkComms.Shutdown()
            Console.WriteLine(ex.ToString())
        End Try

        'When we are done we give the user a chance to see all output
        Console.WriteLine(Environment.NewLine + Environment.NewLine + "Example has completed. Please press any key to close.")
        Console.ReadKey(True)
    End Sub

End Module
