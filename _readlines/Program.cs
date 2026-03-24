using System;
using System.IO;
using System.Text;

var filePath = "C:/src/agsoro/abo2/Abo.Pm/wwwroot/llm-traffic/index.html";
var lines = File.ReadAllLines(filePath, Encoding.UTF8);

void PrintRange(int start, int end) {
    for (int i = start - 1; i < end && i < lines.Length; i++)
        Console.WriteLine($"{i+1}: {lines[i]}");
}

Console.WriteLine("=== renderEntries (1385-1510) ===");
PrintRange(1385, 1510);
