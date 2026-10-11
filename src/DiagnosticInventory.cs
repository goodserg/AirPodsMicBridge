using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AirPodsMicBridge
{
    internal sealed class DiagnosticInventoryResult
    {
        internal IList<Dictionary<string, object>> Drivers { get; private set; }
        internal string Error { get; set; }
        internal bool Truncated { get; set; }

        internal DiagnosticInventoryResult()
        {
            Drivers = new List<Dictionary<string, object>>();
        }
    }

    internal static class DiagnosticInventory
    {
        private const int MaximumDrivers = 128;
        private const int MaximumFieldLength = 512;
        private const int ReturnImmediatelyAndForwardOnly = 48;
        private const int ConnectUseMaximumWait = 128;
        private const string Query =
            "SELECT DeviceName,Manufacturer,DriverProviderName,DriverVersion,DriverDate," +
            "DeviceID,InfName,DeviceClass,IsSigned FROM Win32_PnPSignedDriver " +
            "WHERE DeviceClass='MEDIA' OR DeviceClass='Bluetooth' OR DeviceClass='HIDClass' " +
            "OR DeviceName LIKE '%MagicAAP%' OR DeviceID LIKE 'ROOT\\\\MAGICAAP%'";
        private static readonly string[] StringFields = {
            "DeviceName", "Manufacturer", "DriverProviderName", "DriverVersion",
            "DriverDate", "DeviceID", "InfName", "DeviceClass"
        };

        // Call only on a background worker. Scripting WMI does not expose a timeout
        // for individual enumeration steps; stopping a log must not join this worker.
        internal static DiagnosticInventoryResult Capture()
        {
            DiagnosticInventoryResult result = new DiagnosticInventoryResult();
            object locator = null, services = null, objects = null;
            IEnumerator enumerator = null;
            string stage = "connect";
            try
            {
                locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator", true));
                services = Invoke(locator, "ConnectServer", new object[] {
                    ".", @"root\cimv2", Type.Missing, Type.Missing, Type.Missing,
                    Type.Missing, ConnectUseMaximumWait, Type.Missing
                });
                stage = "query";
                objects = Invoke(services, "ExecQuery", new object[] {
                    Query, "WQL", ReturnImmediatelyAndForwardOnly, Type.Missing
                });
                stage = "create-enumerator";
                enumerator = (IEnumerator)Get(objects, "_NewEnum");
                stage = "enumerate";
                while (enumerator.MoveNext())
                {
                    object driver = enumerator.Current;
                    try
                    {
                        if (result.Drivers.Count >= MaximumDrivers)
                        {
                            result.Truncated = true;
                            break;
                        }
                        stage = "read-properties";
                        Dictionary<string, object> row = new Dictionary<string, object>(StringComparer.Ordinal);
                        foreach (string field in StringFields)
                        {
                            object value = ReadField(driver, field);
                            string text = value == null || value == DBNull.Value ? null :
                                Convert.ToString(value, CultureInfo.InvariantCulture);
                            if (text != null && text.Length > MaximumFieldLength)
                                text = text.Substring(0, MaximumFieldLength);
                            row[field] = text;
                        }
                        // Recheck the class/device whitelist even if a provider ignores its query filter.
                        string deviceClass = row["DeviceClass"] as string;
                        if (!IsSupportedClass(deviceClass) && !IsMagicAap(row)) continue;
                        object signed = ReadField(driver, "IsSigned");
                        row["IsSigned"] = signed == null || signed == DBNull.Value ? null :
                            (object)Convert.ToBoolean(signed, CultureInfo.InvariantCulture);
                        result.Drivers.Add(row);
                    }
                    finally { Release(driver); }
                    stage = "enumerate";
                }
            }
            catch (Exception exception)
            {
                while (exception is TargetInvocationException && exception.InnerException != null)
                    exception = exception.InnerException;
                result.Error = "Windows driver inventory is unavailable (" + stage + ", 0x" +
                    Marshal.GetHRForException(exception).ToString("X8", CultureInfo.InvariantCulture) + ").";
            }
            finally
            {
                IDisposable disposable = enumerator as IDisposable;
                if (disposable != null) disposable.Dispose();
                Release(enumerator);
                Release(objects);
                Release(services);
                Release(locator);
            }
            return result;
        }

        private static bool IsSupportedClass(string deviceClass)
        {
            return String.Equals(deviceClass, "MEDIA", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(deviceClass, "Bluetooth", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(deviceClass, "HIDClass", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMagicAap(Dictionary<string, object> row)
        {
            string name = row["DeviceName"] as string;
            string identifier = row["DeviceID"] as string;
            return (name != null && name.IndexOf("MagicAAP", StringComparison.OrdinalIgnoreCase) >= 0) ||
                (identifier != null && identifier.StartsWith(@"ROOT\MAGICAAP", StringComparison.OrdinalIgnoreCase));
        }

        private static object Invoke(object target, string member, object[] arguments)
        {
            return target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, arguments);
        }

        private static object Get(object target, string member)
        {
            return target.GetType().InvokeMember(member, BindingFlags.GetProperty, null, target, null);
        }

        private static object ReadField(object driver, string field)
        {
            object properties = null, property = null;
            try
            {
                properties = Get(driver, "Properties_");
                property = Invoke(properties, "Item", new object[] { field, Type.Missing });
                return Get(property, "Value");
            }
            finally
            {
                Release(property);
                Release(properties);
            }
        }

        private static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }
    }
}
