﻿using Microsoft.Extensions.Configuration;
using SharpRTSPClient;
using System;

IConfiguration config = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
string rtspUri = config["Uri"];
string userName = config["UserName"];
string password = config["Password"];

using (RTSPClient client = new RTSPClient())
{
    client.NewVideoStream += (sender, e) => Console.WriteLine(e.ToString());
    client.ReceivedVideoData += (sender, e) => Console.Write("*");
    client.NewAudioStream += (sender, e) => Console.WriteLine(e.ToString());
    client.ReceivedAudioData += (sender, e) => Console.Write("+");

    client.Connect(rtspUri, RTPTransport.TCP, userName, password);

    Console.WriteLine("Press any key to exit");
    while (!Console.KeyAvailable)
    {
        System.Threading.Thread.Sleep(250);
    }
}