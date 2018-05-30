﻿/*
 
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at http://mozilla.org/MPL/2.0/.
 
  Copyright (C) 2009-2011 Michael Möller <mmoeller@openhardwaremonitor.org>
	
*/

using System;
using OpenHardwareMonitor.Collections;

namespace OpenHardwareMonitor.Hardware
{
    public abstract class Hardware : IHardware
    {
        protected readonly ListSet<ISensor> active = new ListSet<ISensor>();

        protected Hardware(string name, Identifier identifier)
        {
            Identifier = identifier;
            Name = name;
        }

        public IHardware[] SubHardware => new IHardware[0];

        public virtual IHardware Parent => null;

        public virtual ISensor[] Sensors => active.ToArray();

        public string Name { get; set; }

        public Identifier Identifier { get; }

        public abstract HardwareType HardwareType { get; }

        public virtual string GetReport()
        {
            return null;
        }

        public abstract void Update();

        public void Accept(IVisitor visitor)
        {
            if (visitor == null)
                throw new ArgumentNullException(nameof(visitor));
            visitor.VisitHardware(this);
        }

        public virtual void Traverse(IVisitor visitor)
        {
            foreach (var sensor in active)
                sensor.Accept(visitor);
        }

        public virtual void ActivateSensor(ISensor sensor)
        {
            if (active.Add(sensor))
                SensorAdded?.Invoke(sensor);
        }

        protected virtual void DeactivateSensor(ISensor sensor)
        {
            if (active.Remove(sensor))
                SensorRemoved?.Invoke(sensor);
        }

        public event HardwareEventHandler Closing;

        public virtual void Close()
        {
            Closing?.Invoke(this);
        }

#pragma warning disable 67
        public event SensorEventHandler SensorAdded;
        public event SensorEventHandler SensorRemoved;
#pragma warning restore 67
    }
}