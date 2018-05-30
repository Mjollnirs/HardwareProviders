﻿/*
 
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at http://mozilla.org/MPL/2.0/.
 
  Copyright (C) 2009-2015 Michael Möller <mmoeller@openhardwaremonitor.org>
	Copyright (C) 2010 Paul Werelds
  Copyright (C) 2011 Roland Reinl <roland-reinl@gmx.de>
	
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using OpenHardwareMonitor.Collections;

namespace OpenHardwareMonitor.Hardware.HDD
{
    public abstract class AbstractHarddrive : Hardware
    {
        private const int UPDATE_DIVIDER = 30; // update only every 30s

        // array of all harddrive types, matching type is searched in this order
        private static readonly Type[] hddTypes =
        {
            typeof(SSDPlextor),
            typeof(SSDIntel),
            typeof(SSDSandforce),
            typeof(SSDIndilinx),
            typeof(SSDSamsung),
            typeof(SSDMicron),
            typeof(GenericHarddisk)
        };

        private readonly IntPtr handle;
        private readonly int index;
        private readonly ISmart smart;
        private int count;

        private readonly DriveInfo[] driveInfos;

        private readonly string firmwareRevision;
        private IDictionary<SmartAttribute, Sensor> sensors;

        private readonly IList<SmartAttribute> smartAttributes;
        private Sensor usageSensor;

        protected AbstractHarddrive(ISmart smart, string name,
            string firmwareRevision, int index,
            IEnumerable<SmartAttribute> smartAttributes)
            : base(name, new Identifier("hdd",
                index.ToString(CultureInfo.InvariantCulture)))
        {
            this.firmwareRevision = firmwareRevision;
            this.smart = smart;
            handle = smart.OpenDrive(index);

            if (handle != smart.InvalidHandle)
                smart.EnableSmart(handle, index);

            this.index = index;
            count = 0;

            this.smartAttributes = new List<SmartAttribute>(smartAttributes);

            var logicalDrives = smart.GetLogicalDrives(index);
            var driveInfoList = new List<DriveInfo>(logicalDrives.Length);
            foreach (var logicalDrive in logicalDrives)
                try
                {
                    var di = new DriveInfo(logicalDrive);
                    if (di.TotalSize > 0)
                        driveInfoList.Add(new DriveInfo(logicalDrive));
                }
                catch (ArgumentException)
                {
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

            driveInfos = driveInfoList.ToArray();

            CreateSensors();
        }

        public override HardwareType HardwareType => HardwareType.HDD;

        public static AbstractHarddrive CreateInstance(ISmart smart, int driveIndex)
        {
            var deviceHandle = smart.OpenDrive(driveIndex);

            string name = null;
            string firmwareRevision = null;
            DriveAttributeValue[] values = { };

            if (deviceHandle != smart.InvalidHandle)
            {
                var nameValid = smart.ReadNameAndFirmwareRevision(deviceHandle,
                    driveIndex, out name, out firmwareRevision);
                var smartEnabled = smart.EnableSmart(deviceHandle, driveIndex);

                if (smartEnabled)
                    values = smart.ReadSmartData(deviceHandle, driveIndex);

                smart.CloseHandle(deviceHandle);

                if (!nameValid)
                {
                    name = null;
                    firmwareRevision = null;
                }
            }
            else
            {
                var logicalDrives = smart.GetLogicalDrives(driveIndex);
                if (logicalDrives == null || logicalDrives.Length == 0)
                    return null;

                var hasNonZeroSizeDrive = false;
                foreach (var logicalDrive in logicalDrives)
                    try
                    {
                        var di = new DriveInfo(logicalDrive);
                        if (di.TotalSize > 0)
                        {
                            hasNonZeroSizeDrive = true;
                            break;
                        }
                    }
                    catch (ArgumentException)
                    {
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }

                if (!hasNonZeroSizeDrive)
                    return null;
            }

            if (string.IsNullOrEmpty(name))
                name = "Generic Hard Disk";

            if (string.IsNullOrEmpty(firmwareRevision))
                firmwareRevision = "Unknown";

            foreach (var type in hddTypes)
            {
                // get the array of name prefixes for the current type
                var namePrefixes = type.GetCustomAttributes(
                    typeof(NamePrefixAttribute), true) as NamePrefixAttribute[];

                // get the array of the required SMART attributes for the current type
                var requiredAttributes = type.GetCustomAttributes(
                    typeof(RequireSmartAttribute), true) as RequireSmartAttribute[];

                // check if all required attributes are present
                var allRequiredAttributesFound = true;
                foreach (var requireAttribute in requiredAttributes)
                {
                    var adttributeFound = false;
                    foreach (var value in values)
                        if (value.Identifier == requireAttribute.AttributeId)
                        {
                            adttributeFound = true;
                            break;
                        }

                    if (!adttributeFound)
                    {
                        allRequiredAttributesFound = false;
                        break;
                    }
                }

                // if an attribute is missing, then try the next type
                if (!allRequiredAttributesFound)
                    continue;

                // check if there is a matching name prefix for this type
                foreach (var prefix in namePrefixes)
                    if (name.StartsWith(prefix.Prefix, StringComparison.InvariantCulture))
                        return Activator.CreateInstance(type, smart, name, firmwareRevision, driveIndex) as AbstractHarddrive;
            }

            // no matching type has been found
            return null;
        }

        private void CreateSensors()
        {
            sensors = new Dictionary<SmartAttribute, Sensor>();

            if (handle != smart.InvalidHandle)
            {
                IList<Pair<SensorType, int>> sensorTypeAndChannels =
                    new List<Pair<SensorType, int>>();

                var values = smart.ReadSmartData(handle, index);

                foreach (var attribute in smartAttributes)
                {
                    if (!attribute.SensorType.HasValue)
                        continue;

                    var found = false;
                    foreach (var value in values)
                        if (value.Identifier == attribute.Identifier)
                        {
                            found = true;
                            break;
                        }

                    if (!found)
                        continue;

                    var pair = new Pair<SensorType, int>(
                        attribute.SensorType.Value, attribute.SensorChannel);

                    if (!sensorTypeAndChannels.Contains(pair))
                    {
                        var sensor = new Sensor(attribute.SensorName,
                            attribute.SensorChannel, attribute.DefaultHiddenSensor,
                            attribute.SensorType.Value, this, attribute.ParameterDescriptions);

                        sensors.Add(attribute, sensor);
                        ActivateSensor(sensor);
                        sensorTypeAndChannels.Add(pair);
                    }
                }
            }

            if (driveInfos.Length > 0)
            {
                usageSensor =
                    new Sensor("Used Space", 0, SensorType.Load, this);
                ActivateSensor(usageSensor);
            }
        }

        public virtual void UpdateAdditionalSensors(DriveAttributeValue[] values)
        {
        }

        public override void Update()
        {
            if (count == 0)
            {
                if (handle != smart.InvalidHandle)
                {
                    var values = smart.ReadSmartData(handle, index);

                    foreach (var keyValuePair in sensors)
                    {
                        var attribute = keyValuePair.Key;
                        foreach (var value in values)
                            if (value.Identifier == attribute.Identifier)
                            {
                                var sensor = keyValuePair.Value;
                                sensor.Value = attribute.ConvertValue(value, sensor.Parameters);
                            }
                    }

                    UpdateAdditionalSensors(values);
                }

                if (usageSensor != null)
                {
                    long totalSize = 0;
                    long totalFreeSpace = 0;

                    for (var i = 0; i < driveInfos.Length; i++)
                    {
                        if (!driveInfos[i].IsReady)
                            continue;
                        try
                        {
                            totalSize += driveInfos[i].TotalSize;
                            totalFreeSpace += driveInfos[i].TotalFreeSpace;
                        }
                        catch (IOException)
                        {
                        }
                        catch (UnauthorizedAccessException)
                        {
                        }
                    }

                    if (totalSize > 0)
                        usageSensor.Value = 100.0f - 100.0f * totalFreeSpace / totalSize;
                    else
                        usageSensor.Value = null;
                }
            }

            count++;
            count %= UPDATE_DIVIDER;
        }

        public override string GetReport()
        {
            var r = new StringBuilder();

            r.AppendLine(GetType().Name);
            r.AppendLine();
            r.AppendLine("Drive name: " + Name);
            r.AppendLine("Firmware version: " + firmwareRevision);
            r.AppendLine();

            if (handle != smart.InvalidHandle)
            {
                var values = smart.ReadSmartData(handle, index);
                var thresholds =
                    smart.ReadSmartThresholds(handle, index);

                if (values.Length > 0)
                {
                    r.AppendFormat(CultureInfo.InvariantCulture,
                        " {0}{1}{2}{3}{4}{5}{6}{7}",
                        "ID".PadRight(3),
                        "Description".PadRight(35),
                        "Raw Value".PadRight(13),
                        "Worst".PadRight(6),
                        "Value".PadRight(6),
                        "Thres".PadRight(6),
                        "Physical".PadRight(8),
                        Environment.NewLine);

                    foreach (var value in values)
                    {
                        if (value.Identifier == 0x00)
                            break;

                        byte? threshold = null;
                        foreach (var t in thresholds)
                            if (t.Identifier == value.Identifier)
                                threshold = t.Threshold;

                        var description = "Unknown";
                        float? physical = null;
                        foreach (var a in smartAttributes)
                            if (a.Identifier == value.Identifier)
                            {
                                description = a.Name;
                                if (a.HasRawValueConversion | a.SensorType.HasValue)
                                    physical = a.ConvertValue(value, null);
                                else
                                    physical = null;
                            }

                        var raw = BitConverter.ToString(value.RawValue);
                        r.AppendFormat(CultureInfo.InvariantCulture,
                            " {0}{1}{2}{3}{4}{5}{6}{7}",
                            value.Identifier.ToString("X2").PadRight(3),
                            description.PadRight(35),
                            raw.Replace("-", "").PadRight(13),
                            value.WorstValue.ToString(CultureInfo.InvariantCulture).PadRight(6),
                            value.AttrValue.ToString(CultureInfo.InvariantCulture).PadRight(6),
                            (threshold.HasValue
                                ? threshold.Value.ToString(
                                    CultureInfo.InvariantCulture)
                                : "-").PadRight(6),
                            (physical.HasValue
                                ? physical.Value.ToString(
                                    CultureInfo.InvariantCulture)
                                : "-").PadRight(8),
                            Environment.NewLine);
                    }

                    r.AppendLine();
                }
            }

            foreach (var di in driveInfos)
            {
                if (!di.IsReady)
                    continue;
                try
                {
                    r.AppendLine("Logical drive name: " + di.Name);
                    r.AppendLine("Format: " + di.DriveFormat);
                    r.AppendLine("Total size: " + di.TotalSize);
                    r.AppendLine("Total free space: " + di.TotalFreeSpace);
                    r.AppendLine();
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return r.ToString();
        }

        protected static float RawToInt(byte[] raw, byte value,
            IReadOnlyArray<IParameter> parameters)
        {
            return (raw[3] << 24) | (raw[2] << 16) | (raw[1] << 8) | raw[0];
        }

        public override void Close()
        {
            if (handle != smart.InvalidHandle)
                smart.CloseHandle(handle);

            base.Close();
        }

        public override void Traverse(IVisitor visitor)
        {
            foreach (var sensor in Sensors)
                sensor.Accept(visitor);
        }
    }
}