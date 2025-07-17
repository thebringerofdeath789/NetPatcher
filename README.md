This program is a .NET patcher that allows users to modify .NET applications to execute specific actions. The program will find compatible binaries in the Program Files, 
Program Files (x86) or user profile directories, and then applies the specified patch based on user input. The patched executable will be copied to the current directory with
a new name. This version uses dnlib for binary manipulation. The patched program will execute the injected payload immediately when started. After the payload, the original 
program logic resumes, unless the payload itself interrupts or terminates execution (for example, if the payload throws an exception or calls Environment.Exit).
