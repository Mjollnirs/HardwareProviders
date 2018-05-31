﻿/*
 
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at http://mozilla.org/MPL/2.0/.
 
  Copyright (C) 2010-2014 Michael Möller <mmoeller@openhardwaremonitor.org>
	
*/

namespace HardwareProviders
{
    public delegate void ControlEventHandler(Control control);

    public class Control : IControl
    {
        private ControlMode mode;
        private float softwareValue;

        public Control(float minSoftwareValue, float maxSoftwareValue)
        {
            MinSoftwareValue = minSoftwareValue;
            MaxSoftwareValue = maxSoftwareValue;

            softwareValue = 0;
            mode = ControlMode.Undefined;
        }

        public ControlMode ControlMode
        {
            get => mode;
            private set
            {
                if (mode != value)
                {
                    mode = value;
                    ControlModeChanged?.Invoke(this);
                }
            }
        }

        public float SoftwareValue
        {
            get => softwareValue;
            private set
            {
                if (softwareValue != value)
                {
                    softwareValue = value;
                    SoftwareControlValueChanged?.Invoke(this);
                }
            }
        }

        public void SetDefault()
        {
            ControlMode = ControlMode.Default;
        }

        public float MinSoftwareValue { get; }

        public float MaxSoftwareValue { get; }

        public void SetSoftware(float value)
        {
            ControlMode = ControlMode.Software;
            SoftwareValue = value;
        }

        public event ControlEventHandler ControlModeChanged;
        public event ControlEventHandler SoftwareControlValueChanged;
    }
}