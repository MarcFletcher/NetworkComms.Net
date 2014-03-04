' Copyright 2009-2014 Marc Fletcher, Matthew Dean
' 
' This program is free software: you can redistribute it and/or modify
' it under the terms of the GNU General Public License as published by
' the Free Software Foundation, either version 3 of the License, or
' (at your option) any later version.
' 
' This program is distributed in the hope that it will be useful,
' but WITHOUT ANY WARRANTY; without even the implied warranty of
' MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
' GNU General Public License for more details.
' 
' You should have received a copy of the GNU General Public License
' along with this program.  If not, see <http://www.gnu.org/licenses/>.
' 
' A commercial license of this software can also be purchased. 
' Please see <http://www.networkcomms.net/licensing/> for details.

Imports NetworkCommsDotNet
Imports NetworkCommsDotNet.Tools

Module Program

    Sub Main()
        Try
            Console.SetBufferSize(120, 200)
            Console.SetWindowSize(120, 25)
        Catch ex As NotImplementedException
        End Try

        Thread.CurrentThread.Name = "MainThread"

        Console.WriteLine("Initiating NetworkCommsDotNet examples." + Environment.NewLine)

        'Ask user if they want to enable comms logging
        SelectLogging()

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
            LogTools.LogException(ex, "ExampleError")
            NetworkComms.Shutdown()
            Console.WriteLine(ex.ToString())
        End Try

        'When we are done we give the user a chance to see all output
        Console.WriteLine(Environment.NewLine + Environment.NewLine + "Example has completed. Please press any key to close.")
        Console.ReadKey(True)
    End Sub

    Sub SelectLogging()
        ''If the user wants to enable logging 
        Console.WriteLine("To enable comms logging press 'y'. To leave logging disabled and continue press any other key." + Environment.NewLine)

        If (Console.ReadKey(True).Key = ConsoleKey.Y) Then
            'For logging you can either use the included LiteLogger or create your own by
            'implementing ILogger

            ''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''
            '''' SIMPLE CONSOLE ONLY LOGGING
            ''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''
            'Dim logger = New LiteLogger(LiteLogger.LogMode.ConsoleOnly)
            'NetworkComms.EnableLogging(logger)

            ''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''
            '''' THE FOLLOWING CONFIG LOGS TO BOTH A FILE AND CONSOLE
            ''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''
            Dim logFileName = "ExamplesConsoleLog_" + NetworkComms.NetworkIdentifier.ToString() + ".txt"
            Dim logger = New LiteLogger(LiteLogger.LogMode.ConsoleAndLogFile, logFileName)
            NetworkComms.EnableLogging(logger)

            'We can write to our logger from an external program as well
            NetworkComms.Logger.Info("NetworkComms.Net logging enabled. ALL output directed to console and log file, " + logFileName + Environment.NewLine)
        End If
    End Sub

End Module
