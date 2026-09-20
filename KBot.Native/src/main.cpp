#include <iostream>
#include "../Core/ProcessManager.h"
#include "../IPC/NamedPipeServer.h"

int main()
{
	std::cout << "================================\n";
	std::cout << "           KBot Native\n";
	std::cout << "================================\n\n";

	std::string target;
	std::cout << "Target: " << target << "\n\n";

	ProcessManager pm(target);
	std::cout << "[+] Searching for client...\n\n";
	if (pm.IsRunning())
	{
		std::cout << "[+] Client found\n    PID: " << pm.GetPid() << "\n";
	}
	else
	{
		std::cout << "[-] Client not found\n";
	}

	NamedPipeServer server("KBot.NativePipe.CharacterV3", &pm);
	server.Run();

	return 0;
}
