﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Common;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Security.Cryptography;


namespace Microsoft.Azure.Devices.E2ETests
{
    [TestClass]
    public partial class X509E2ETests
    {
        private const string DevicePrefix = "E2E_X509_CSharp_";
        private static string hubConnectionString;
        private static string hostName;
        private static RegistryManager registryManager;

        private readonly SemaphoreSlim sequentialTestSemaphore = new SemaphoreSlim(1, 1);

        public TestContext TestContext { get; set; }

        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            var environment = TestUtil.InitializeEnvironment(DevicePrefix);
            hubConnectionString = environment.Item1;
            registryManager = environment.Item2;
            hostName = TestUtil.GetHostName(hubConnectionString);
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            TestUtil.UnInitializeEnvironment(registryManager).GetAwaiter().GetResult();
        }


#if NETCOREAPP2_0
        [TestInitialize]
        public async Task Initialize()
        {
            await sequentialTestSemaphore.WaitAsync();
        }
#else
        [TestInitialize]
        public void Initialize()
        {
            sequentialTestSemaphore.Wait();
        }

#endif

        [TestCleanup]
        public void Cleanup()
        {
            sequentialTestSemaphore.Release(1);
        }

        [TestMethod]
        [TestCategory("X509-Message-E2E")]
        public async Task X509_DeviceSendSingleMessage_Amqp()
        {
            await SendSingleMessageX509(Client.TransportType.Amqp_Tcp_Only);
        }

#if NETCOREAPP2_0
        // x509 not supported by client over MQTT-WS AMQP-WS and Http
        [Ignore]
#endif
        [TestMethod]
        [TestCategory("X509-Message-E2E")]
        public async Task X509_DeviceSendSingleMessage_AmqpWs()
        {
            await SendSingleMessageX509(Client.TransportType.Amqp_WebSocket_Only);
        }

        [TestMethod]
        [TestCategory("X509-Message-E2E")]
        public async Task X509_DeviceSendSingleMessage_Mqtt()
        {
            await SendSingleMessageX509(Client.TransportType.Mqtt_Tcp_Only);
        }

#if NETCOREAPP2_0
        // x509 not supported by client over MQTT-WS AMQP-WS and Http
        [Ignore]
#endif
        [TestMethod]
        [TestCategory("X509-Message-E2E")]
        public async Task X509_DeviceSendSingleMessage_MqttWs()
        {
            await SendSingleMessageX509(Client.TransportType.Mqtt_WebSocket_Only);
        }

#if NETCOREAPP2_0
        // x509 not supported by client over MQTT-WS AMQP-WS and Http
        [Ignore]
#endif
        [TestMethod]
        [TestCategory("X509-Message-E2E")]
        public async Task X509_DeviceSendSingleMessage_Http()
        {
            await SendSingleMessageX509(Client.TransportType.Http1);
        }

        [TestMethod]
        [TestCategory("X509-Message-E2E")]
        public async Task X509_DeviceReceiveSingleMessage_Amqp()
        {
            await ReceiveSingleMessageX509(Client.TransportType.Amqp_Tcp_Only);
        }

        [Ignore] // TODO: #171 - X509 tests are intermittently failing during CI.
        [TestMethod]
        [TestCategory("X509-Message-E2E")]
        public async Task X509_DeviceReceiveSingleMessage_AmqpWs()
        {
            await ReceiveSingleMessageX509(Client.TransportType.Amqp_WebSocket_Only);
        }

        [TestMethod]
        [TestCategory("X509-Message-E2E")]
        public async Task X509_DeviceReceiveSingleMessage_Mqtt()
        {
            await ReceiveSingleMessageX509(Client.TransportType.Mqtt_Tcp_Only);
        }


        [Ignore] // TODO: #171 - X509 tests are intermittently failing during CI.
        [TestMethod]
        [TestCategory("X509-Message-E2E")]
        public async Task X509_DeviceReceiveSingleMessage_MqttWs()
        {
            await ReceiveSingleMessageX509(Client.TransportType.Mqtt_WebSocket_Only);
        }


        [Ignore] // TODO: #171 - X509 tests are intermittently failing during CI.
        [TestMethod]
        [TestCategory("X509-Message-E2E")]
        public async Task X509_DeviceReceiveSingleMessage_Http()
        {
            await ReceiveSingleMessageX509(Client.TransportType.Http1);
        }

        private Client.Message ComposeD2CTestMessage(out string payload, out string p1Value)
        {
            payload = Guid.NewGuid().ToString();
            p1Value = Guid.NewGuid().ToString();

            return new Client.Message(Encoding.UTF8.GetBytes(payload))
            {
                Properties = { ["property1"] = p1Value }
            };
        }


        private Message ComposeC2DTestMessage(out string payload, out string messageId, out string p1Value)
        {
            payload = Guid.NewGuid().ToString();
            messageId = Guid.NewGuid().ToString();
            p1Value = Guid.NewGuid().ToString();

            return new Message(Encoding.UTF8.GetBytes(payload))
            {
                MessageId = messageId,
                Properties = { ["property1"] = p1Value }
            };
        }

        private async Task VerifyReceivedC2DMessage(Client.TransportType transport, DeviceClient deviceClient, string payload, string p1Value)
        {
            var wait = true;

            Stopwatch sw = new Stopwatch();
            sw.Start();
            while (wait)
            {
                Client.Message receivedMessage;

                if (transport == Client.TransportType.Http1)
                {
                    // Long-polling is not supported in http
                    receivedMessage = await deviceClient.ReceiveAsync();
                }
                else
                {
                    receivedMessage = await deviceClient.ReceiveAsync(TimeSpan.FromSeconds(1));
                }

                if (receivedMessage != null)
                {
                    string messageData = Encoding.ASCII.GetString(receivedMessage.GetBytes());
                    Assert.AreEqual(messageData, payload);

                    Assert.AreEqual(receivedMessage.Properties.Count, 1);
                    var prop = receivedMessage.Properties.Single();
                    Assert.AreEqual(prop.Key, "property1");
                    Assert.AreEqual(prop.Value, p1Value);

                    await deviceClient.CompleteAsync(receivedMessage);
                    wait = false;
                }

                if (sw.Elapsed.TotalSeconds > 5)
                {
                    throw new TimeoutException("Test is running longer than expected.");
                }
            }
            sw.Stop();
        }

        // This function create a device with x509 cert and connect to the iothub on the transport specified.
        // Then a message is send from the service client.  
        // It then verifies the message is received on the device.
        private async Task ReceiveSingleMessageX509(Client.TransportType transport)
        {
            Tuple<string, string> deviceInfo = TestUtil.CreateDeviceWithX509(DevicePrefix, hostName, registryManager);
            ServiceClient serviceClient = ServiceClient.CreateFromConnectionString(hubConnectionString);

            X509Certificate2 cert = Configuration.IoTHub.GetCertificateWithPrivateKey();

            var auth = new DeviceAuthenticationWithX509Certificate(deviceInfo.Item1, cert);
            var deviceClient = DeviceClient.Create(deviceInfo.Item2, auth, transport);

            try
            {
                await deviceClient.OpenAsync();

                if (transport == Client.TransportType.Mqtt_Tcp_Only ||
                    transport == Client.TransportType.Mqtt_WebSocket_Only)
                {
                    // Dummy ReceiveAsync to ensure mqtt subscription registration before SendAsync() is called on service client.
                    await deviceClient.ReceiveAsync(TimeSpan.FromSeconds(2));
                }

                string payload, messageId, p1Value;
                await serviceClient.OpenAsync();
                await serviceClient.SendAsync(deviceInfo.Item1,
                    ComposeC2DTestMessage(out payload, out messageId, out p1Value));

                await VerifyReceivedC2DMessage(transport, deviceClient, payload, p1Value);
            }
            finally
            {
                await deviceClient.CloseAsync();
                await serviceClient.CloseAsync();
                await TestUtil.RemoveDeviceAsync(deviceInfo.Item1, registryManager);
            }
        }
    }
}
