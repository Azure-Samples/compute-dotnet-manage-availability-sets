// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Samples.Common;
using Azure.ResourceManager.Storage;
using Azure.ResourceManager.Storage.Models;
using Azure.ResourceManager.TrafficManager;

namespace ManageAvailabilitySet
{
    public class Program
    {
        private static readonly string UserName = Utilities.CreateUsername();
        private static readonly string Password = Utilities.CreatePassword();
        
        /**
         * Azure Compute sample for managing availability sets -
         *  - Create an availability set
         *  - Create a VM in a new availability set
         *  - Create another VM in the same availability set
         *  - Update the availability set
         *  - Create another availability set
         *  - List availability sets
         *  - Delete an availability set.
         */
        public static async Task RunSampleAsync(ArmClient client)
        {
            var region = AzureLocation.EastUS;
            string rgName = Utilities.CreateRandomName("rgCOMA");
            string availSetName1 = Utilities.CreateRandomName("av1");
            string availSetName2 = Utilities.CreateRandomName("av2");
            string pipName = Utilities.CreateRandomName("pip1");
            string vm1Name = Utilities.CreateRandomName("vm1");
            string vm2Name = Utilities.CreateRandomName("vm2");
            string vnetName = Utilities.CreateRandomName("vnet");
            var lro = await client.GetDefaultSubscription().GetResourceGroups().CreateOrUpdateAsync(Azure.WaitUntil.Completed, rgName, new ResourceGroupData(AzureLocation.EastUS));
            var resourceGroup = lro.Value;
            try
            {
                //=============================================================
                // Create an availability set

                Utilities.Log("Creating an availability set");

                var availitySet = resourceGroup.GetAvailabilitySets();
                var availSetData = new AvailabilitySetData(region)
                {
                    PlatformFaultDomainCount = 2,
                    PlatformUpdateDomainCount = 4,
                    Sku = new ComputeSku()
                    {
                        Tier = "AvailabilitySetSkuTypes.Aligned"
                    },
                    Tags =
                    {
                        new KeyValuePair<string, string>("cluster", "Windowslinux"),
                        new KeyValuePair<string, string>("tag1", "tag1val")
                    }
                };
                var availSetResource1 = (await availitySet.CreateOrUpdateAsync(WaitUntil.Completed, availSetName1, availSetData)).Value;

                Utilities.Log("Created first availability set: " + availSetResource1.Id);
                Utilities.PrintAvailabilitySet(availSetResource1);

                //=============================================================
                // Define a virtual network for the VMs in this availability set
                var networkCollection = resourceGroup.GetVirtualNetworks();
                var networkData = new VirtualNetworkData()
                {
                    Location = region,
                    AddressPrefixes =
                    {
                        "10.0.0.0/28"
                    }
                };
                var networkResource = (await networkCollection.CreateOrUpdateAsync(Azure.WaitUntil.Completed, vnetName, networkData)).Value;

                //=============================================================
                // Create a Windows VM in the new availability set

                Utilities.Log("Creating a Windows VM in the availability set");

                //Create a subnet
                Utilities.Log("Creating a Linux subnet...");
                var subnetName = Utilities.CreateRandomName("subnet_");
                var subnetData = new SubnetData()
                {
                    ServiceEndpoints =
                    {
                        new ServiceEndpointProperties()
                        {
                            Service = "Microsoft.Storage"
                        }
                    },
                    Name = subnetName,
                    AddressPrefix = "10.0.0.0/28",
                };
                var subnetLRro = await networkResource.GetSubnets().CreateOrUpdateAsync(WaitUntil.Completed, subnetName, subnetData);
                var subnet = subnetLRro.Value;
                Utilities.Log("Created a Linux subnet with name : " + subnet.Data.Name);

                // Create a public IP address
                Utilities.Log("Creating a Linux Public IP address...");
                var publicAddressIPCollection = resourceGroup.GetPublicIPAddresses();
                var publicIPAddressdata = new PublicIPAddressData()
                {
                    Location = region,
                    Sku = new PublicIPAddressSku()
                    {
                        Name = PublicIPAddressSkuName.Standard,
                    },
                    PublicIPAddressVersion = NetworkIPVersion.IPv4,
                    PublicIPAllocationMethod = NetworkIPAllocationMethod.Static,
                };
                var publicIPAddressLro = await publicAddressIPCollection.CreateOrUpdateAsync(WaitUntil.Completed, pipName, publicIPAddressdata);
                var publicIPAddress = publicIPAddressLro.Value;

                //Create a networkInterface
                Utilities.Log("Created a linux networkInterface");
                var networkInterfaceData = new NetworkInterfaceData()
                {
                    Location = region,
                    IPConfigurations =
                    {
                        new NetworkInterfaceIPConfigurationData()
                        {
                            Name = "internal",
                            Primary = true,
                            Subnet = new SubnetData
                            {
                                Name = subnetName,
                                Id = new ResourceIdentifier($"{networkResource.Data.Id}/subnets/{subnetName}")
                            },
                            PrivateIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                            PublicIPAddress = publicIPAddress.Data,
                        }
                    }
                };
                var networkInterfaceName = Utilities.CreateRandomName("networkInterface");
                var nic = (await resourceGroup.GetNetworkInterfaces().CreateOrUpdateAsync(WaitUntil.Completed, networkInterfaceName, networkInterfaceData)).Value;
                Utilities.Log("Created a Linux networkInterface with name : " + nic.Data.Name);
                //Create a VM with the Public IP address
                Utilities.Log("Creating a zonal VM with implicitly zoned related resources (PublicIP, Disk)");
                var virtualMachineCollection = resourceGroup.GetVirtualMachines();
                var windowsComputerName = Utilities.CreateRandomName("windowsComputer");
                var windowsVmdata = new VirtualMachineData(region)
                {
                    HardwareProfile = new VirtualMachineHardwareProfile()
                    {
                        VmSize = "Standard_D2a_v4"
                    },
                    OSProfile = new VirtualMachineOSProfile()
                    {
                        AdminUsername = UserName,
                        AdminPassword = Password,
                        ComputerName = windowsComputerName,
                    },
                    NetworkProfile = new VirtualMachineNetworkProfile()
                    {
                        NetworkInterfaces =
                        {
                            new VirtualMachineNetworkInterfaceReference()
                            {
                                Id = nic.Id,
                                Primary = true,
                            }
                        }
                    },
                    StorageProfile = new VirtualMachineStorageProfile()
                    {
                        OSDisk = new VirtualMachineOSDisk(DiskCreateOptionType.FromImage)
                        {
                            OSType = SupportedOperatingSystemType.Linux,
                            Caching = CachingType.ReadWrite,
                            ManagedDisk = new VirtualMachineManagedDisk()
                            {
                                StorageAccountType = StorageAccountType.StandardLrs
                            }
                        },
                        ImageReference = new ImageReference()
                        {
                            Publisher = "Canonical",
                            Offer = "WindowsServer",
                            Sku = "2012R2Datacenter",
                            Version = "latest",
                        },
                    },
                    Zones =
                    {
                        "1"
                    },
                    AvailabilitySetId = availSetResource1.Id
                };
                var virtualMachine1Lro = await virtualMachineCollection.CreateOrUpdateAsync(WaitUntil.Completed, vm1Name, windowsVmdata);
                var virtualMachine1 = virtualMachine1Lro.Value;

                Utilities.Log("Created first VM:" + virtualMachine1.Id);
                Utilities.PrintVirtualMachine(virtualMachine1);

                //=============================================================
                // Create a Linux VM in the same availability set

                Utilities.Log("Creating a Linux VM in the availability set");

                var linuxComputerName = Utilities.CreateRandomName("linuxComputer");
                var linuxVmdata = new VirtualMachineData(region)
                {
                    HardwareProfile = new VirtualMachineHardwareProfile()
                    {
                        VmSize = "Standard_D2a_v4"
                    },
                    OSProfile = new VirtualMachineOSProfile()
                    {
                        AdminUsername = UserName,
                        AdminPassword = Password,
                        ComputerName = linuxComputerName,
                    },
                    NetworkProfile = new VirtualMachineNetworkProfile()
                    {
                        NetworkInterfaces =
                        {
                            new VirtualMachineNetworkInterfaceReference()
                            {
                                Id = nic.Id,
                                Primary = true,
                            }
                        }
                    },
                    StorageProfile = new VirtualMachineStorageProfile()
                    {
                        OSDisk = new VirtualMachineOSDisk(DiskCreateOptionType.FromImage)
                        {
                            OSType = SupportedOperatingSystemType.Linux,
                            Caching = CachingType.ReadWrite,
                            ManagedDisk = new VirtualMachineManagedDisk()
                            {
                                StorageAccountType = StorageAccountType.StandardLrs
                            }
                        },
                        ImageReference = new ImageReference()
                        {
                            Publisher = "Canonical",
                            Offer = "UbuntuServer",
                            Sku = "16_04_Lts",
                            Version = "latest",
                        },
                    },
                    Zones =
                    {
                        "1"
                    },
                    AvailabilitySetId = availSetResource1.Id
                };
                var virtualMachine2Lro = await virtualMachineCollection.CreateOrUpdateAsync(WaitUntil.Completed, vm2Name, linuxVmdata);
                var virtualMachine2 = virtualMachine2Lro.Value;

                Utilities.Log("Created second VM: " + virtualMachine2.Id);
                Utilities.PrintVirtualMachine(virtualMachine2);

                //=============================================================
                // Update - Tag the availability set

                await availSetResource1.UpdateAsync(new AvailabilitySetPatch()
                {
                    Tags =
                    {
                        new KeyValuePair<string, string>("server1", "nginx"),
                        new KeyValuePair<string, string>("server2", "iis")
                    },
                });
                await availSetResource1.RemoveTagAsync("tag1");

                Utilities.Log("Tagged availability set: " + availSetResource1.Id);

                //=============================================================
                // Create another availability set

                Utilities.Log("Creating an availability set");

                var availSet2Data = new AvailabilitySetData(region)
                {
                };
                var availSetResource2 = (await availitySet.CreateOrUpdateAsync(WaitUntil.Completed, availSetName2, availSet2Data)).Value;

                Utilities.Log("Created second availability set: " + availSetResource2.Id);
                Utilities.PrintAvailabilitySet(availSetResource2);

                //=============================================================
                // List availability sets

                var resourceGroupName = availSetResource1.Id.ResourceGroupName;

                Utilities.Log("Printing list of availability sets  =======");

                await foreach (var availabilitySet in availitySet.GetAllAsync())
                {
                    Utilities.PrintAvailabilitySet(availabilitySet);
                }

                //=============================================================
                // Delete an availability set

                Utilities.Log("Deleting an availability set: " + availSetResource2.Id);

                availSetResource2.Delete(WaitUntil.Completed);

                Utilities.Log("Deleted availability set: " + availSetResource2.Id);
            }
            finally
            {
                try
                {
                    Utilities.Log("Deleting Resource Group: " + rgName);
                    await resourceGroup.DeleteAsync(WaitUntil.Completed);
                    Utilities.Log("Deleted Resource Group: " + rgName);
                }
                catch (Exception ex)
                {
                    Utilities.Log(ex);
                }
            }
        }

        public static async Task Main(string[] args)
        {
            try
            {
                //=============================================================
                // Authenticate
                var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
                var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");
                var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
                var subscription = Environment.GetEnvironmentVariable("SUBSCRIPTION_ID");
                ClientSecretCredential credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
                ArmClient client = new ArmClient(credential, subscription);

                // Print selected subscription
                Utilities.Log("Selected subscription: " + client.GetSubscriptions().Id);

                await RunSampleAsync(client);
            }
            catch (Exception ex)
            {
                Utilities.Log(ex);
            }
        }
    }
}