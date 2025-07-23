This program is a .NET patcher that allows users to modify .NET applications to execute specific actions. The program will find compatible binaries in the Program Files, 
Program Files (x86) or user profile directories, and then applies the specified patch based on user input. The patched executable will be copied to the current directory with
a new name. This version uses dnlib for binary manipulation. The patched program will execute the injected payload immediately when started. After the payload, the original 
program logic resumes, unless the payload itself interrupts or terminates execution (for example, if the payload throws an exception or calls Environment.Exit).

For option #4 use InjectedThreadPayload from my repo.
Option 4 will:
1) Patch the selected DLL or EXE
2) The patched executable will first select a process to inject a thread to (that it has permission to inject)
3) InjecectedThreadPayload will run in the context of the selected process
4) InjectedThreadPayload will start a socks proxy under the context of the process (bypassing the firewall if that process is exempt)
5) Display a messagebox with the context the dll is running as
6) resume normal program operation
   
