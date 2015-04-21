﻿/*
Ferram Aerospace Research v0.14.6
Copyright 2014, Michael Ferrara, aka Ferram4

    This file is part of Ferram Aerospace Research.

    Ferram Aerospace Research is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    Ferram Aerospace Research is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with Ferram Aerospace Research.  If not, see <http://www.gnu.org/licenses/>.

    Serious thanks:		a.g., for tons of bugfixes and code-refactorings
            			Taverius, for correcting a ton of incorrect values
            			sarbian, for refactoring code for working with MechJeb, and the Module Manager 1.5 updates
            			ialdabaoth (who is awesome), who originally created Module Manager
                        Regex, for adding RPM support
            			Duxwing, for copy editing the readme
 * 
 * Kerbal Engineer Redux created by Cybutek, Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported License
 *      Referenced for starting point for fixing the "editor click-through-GUI" bug
 *
 * Part.cfg changes powered by sarbian & ialdabaoth's ModuleManager plugin; used with permission
 *	http://forum.kerbalspaceprogram.com/threads/55219
 *
 * Toolbar integration powered by blizzy78's Toolbar plugin; used with permission
 *	http://forum.kerbalspaceprogram.com/threads/60863
 */

using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using UnityEngine;
using FerramAerospaceResearch.FARPartGeometry;
using ferram4;

namespace FerramAerospaceResearch.FARAeroComponents
{
    public class FARVesselAero : MonoBehaviour
    {
        Vessel _vessel;
        VesselType _vType;
        int _voxelCount;

        VehicleVoxel _voxel = null;
        VoxelCrossSection[] _vehicleCrossSection = null;

        bool ready = false;

        public double Length
        {
            get { return _vehicleAero.Length; }
        }

        public double MaxCrossSectionArea
        {
            get { return _vehicleAero.MaxCrossSectionArea; }
        }

        double machNumber;
        public double MachNumber
        {
            get { return machNumber; }
        }
        double reynoldsNumber;
        public double ReynoldsNumber
        {
            get { return reynoldsNumber; }
        }

        List<FARAeroPartModule> _currentAeroModules;
        List<FARAeroSection> _currentAeroSections;

        int _updateRateLimiter = 20;
        bool _updateQueued = true;

        VehicleAerodynamics _vehicleAero;

        private void Awake()
        {
            _vessel = gameObject.GetComponent<Vessel>();
            VesselUpdate();
            this.enabled = true;
            for(int i = 0; i < _vessel.Parts.Count; i++)
            {
                Part p = _vessel.Parts[i];
                p.maximum_drag = 0;
                p.minimum_drag = 0;
                p.angularDrag = 0;
            }
        }

        private void FixedUpdate()
        {
            if (_vehicleAero.CalculationCompleted)
            {
                _vehicleAero.GetNewAeroData(out _currentAeroModules, out _currentAeroSections);

                _vessel.SendMessage("UpdateAeroModules", _currentAeroModules);
            } 
            
            if (FlightGlobals.ready && _currentAeroSections != null)
            {
                float atmDensity = (float)_vessel.atmDensity;

                if (atmDensity <= 0)
                    return;

                machNumber = FARAeroUtil.GetMachNumber(_vessel.mainBody, _vessel.altitude, _vessel.srfSpeed);
                reynoldsNumber = FARAeroUtil.CalculateReynoldsNumber(_vessel.atmDensity, Length, _vessel.srfSpeed, machNumber, FlightGlobals.getExternalTemperature((float)_vessel.altitude, _vessel.mainBody) + 273.15f);
                float skinFrictionDragCoefficient = (float)FARAeroUtil.SkinFrictionDrag(reynoldsNumber, machNumber);

                Vector3 frameVel = Krakensbane.GetFrameVelocityV3f();

                for (int i = 0; i < _currentAeroModules.Count; i++)
                {
                    FARAeroPartModule m = _currentAeroModules[i];
                    if ((object)m != null)
                        m.UpdateVelocityAndAngVelocity(frameVel);
                }
                
                for (int i = 0; i < _currentAeroSections.Count; i++)
                    _currentAeroSections[i].CalculateAeroForces(atmDensity, (float)machNumber, (float)(reynoldsNumber / Length), skinFrictionDragCoefficient);

                for (int i = 0; i < _currentAeroModules.Count; i++)
                {
                    FARAeroPartModule m = _currentAeroModules[i];
                    if ((object)m != null)
                        m.ApplyForces();
                }

                if (_updateRateLimiter < 20)
                {
                    _updateRateLimiter++;
                }
                else if (_updateQueued)
                    VesselUpdate();
            }
        }

        public void AnimationVoxelUpdate()
        {
            if (_updateRateLimiter == 20)
                _updateRateLimiter = 18;
            VesselUpdate();
        }

        public void VesselUpdate()
        {
            if(_vessel == null)
                _vessel = gameObject.GetComponent<Vessel>();
            if (_vehicleAero == null)
                _vehicleAero = new VehicleAerodynamics();

            if (_updateRateLimiter < 20)        //this has been updated recently in the past; queue an update and return
            {
                _updateQueued = true;
                return;
            }
            else                                //last update was far enough in the past to run; reset rate limit counter and clear the queued flag
            {
                _updateRateLimiter = 0;
                _updateQueued = false;
            }
            _vType = _vessel.vesselType;

            _voxelCount = VoxelCountFromType();


            _vehicleAero.VoxelUpdate(_vessel.transform.worldToLocalMatrix, _vessel.transform.localToWorldMatrix, CalculateVesselMainAxis(), _voxelCount, _vessel.Parts);

            Debug.Log("Updating vessel voxel for " + _vessel.vesselName);
        }

        //TODO: have this grab from a config file
        private int VoxelCountFromType()
        {
            if (_vType == VesselType.Debris || _vType == VesselType.Unknown)
                return 20000;
            else
                return 125000;
        }

        private Vector3 CalculateVesselMainAxis()
        {
            Vector3 axis = Vector3.zero;
            List<Part> vesselPartsList = _vessel.Parts;
            for(int i = 0; i < vesselPartsList.Count; i++)      //get axis by averaging all parts up vectors
            {
                Part p = vesselPartsList[i];
                GeometryPartModule m = p.GetComponent<GeometryPartModule>();
                if(m != null)
                {
                    Bounds b = m.overallMeshBounds;
                    axis += p.transform.up * b.size.x * b.size.y * b.size.z;    //scale part influence by approximate size
                }
            }
            axis.Normalize();   //normalize axis for later calcs
            float dotProd;

            dotProd = Math.Abs(Vector3.Dot(axis, _vessel.transform.up));
            if (dotProd >= 0.99)        //if axis and _vessel.up are nearly aligned, just use _vessel.up
                return Vector3.up;

            dotProd = Math.Abs(Vector3.Dot(axis, _vessel.transform.forward));

            if (dotProd >= 0.99)        //Same for forward...
                return Vector3.forward;

            dotProd = Math.Abs(Vector3.Dot(axis, _vessel.transform.right));

            if (dotProd >= 0.99)        //and right...
                return Vector3.right;

            //Otherwise, now we need to use axis, since it's obviously not close to anything else

            axis = _vessel.transform.worldToLocalMatrix.MultiplyVector(axis);

            return axis;
        }
    }
}
