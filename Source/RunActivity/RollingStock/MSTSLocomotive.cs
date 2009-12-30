﻿/* LOCOMOTIVE CLASSES
 * 
 * Used a a base for Steam, Diesel and Electric locomotive classes.
 * 
 * A locomotive is represented by two classes:
 *  LocomotiveSimulator - defines the behaviour, ie physics, motion, power generated etc
 *  LocomotiveViewer - defines the appearance in a 3D viewer including animation for wipers etc
 *  
 * Both these classes derive from corresponding classes for a basic TrainCar
 *  TrainCarSimulator - provides for movement, rolling friction, etc
 *  TrainCarViewer - provides basic animation for running gear, wipers, etc
 *  
 * Locomotives can either be controlled by a player, 
 * or controlled by the train's MU signals for brake and throttle etc.
 * The player controlled loco generates the MU signals which pass along to every
 * unit in the train.
 * For AI trains, the AI software directly generates the MU signals - there is no
 * player controlled train.
 * 
 * The end result of the physics calculations for the the locomotive is
 * a TractiveForce and a FrictionForce ( generated by the TrainCar class )
 * 
 */
/// COPYRIGHT 2009 by the Open Rails project.
/// This code is provided to enable you to contribute improvements to the open rails program.  
/// Use of the code for any other purpose or distribution of the code to anyone else
/// is prohibited without specific written permission from admin@openrails.org.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MSTS;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework;
using System.IO;



namespace ORTS
{

    ///////////////////////////////////////////////////
    ///   SIMULATION BEHAVIOUR
    ///////////////////////////////////////////////////


    /// <summary>
    /// Adds Throttle, Direction, Horn, Sander and Wiper control
    /// to the basic TrainCar.
    /// Use as a base for Electric, Diesel or Steam locomotives.
    /// </summary>
    public class MSTSLocomotive: MSTSWagon
    {
        // simulation parameters
        public bool Horn = false;
        public bool Bell = false;
        public bool Sander = false;  
        public bool Wiper = false;   

        // wag file data
        public string CabSoundFileName = null;
        public string CVFFileName = null;

        public CVFFile CVFFile = null;


        public MSTSLocomotive(string  wagPath)
            : base(wagPath)
        {
        }

        /// <summary>
        /// This initializer is called when we haven't loaded this type of car before
        /// and must read it new from the wag file.
        /// </summary>
        public override void InitializeFromWagFile(string wagFilePath)
        {
            base.InitializeFromWagFile(wagFilePath);

            if (CVFFileName != null)
            {
                string CVFFilePath = Path.GetDirectoryName(WagFilePath) + @"\CABVIEW\" + CVFFileName;
                CVFFile = new CVFFile(CVFFilePath);

                // Set up camera locations for the cab views
                for( int i = 0; i < CVFFile.Locations.Count; ++i )
                {
                    if (i >= CVFFile.Locations.Count || i >= CVFFile.Directions.Count)
                    {
                        Console.Error.WriteLine("Position or Direction missing in " + CVFFilePath);
                        break;
                    }
                    ViewPoint viewPoint = new ViewPoint();
                    viewPoint.Location = CVFFile.Locations[i];
                    viewPoint.StartDirection = CVFFile.Directions[i];
                    viewPoint.RotationLimit = new Vector3( 0,0,0 );  // cab views have a fixed head position
                    FrontCabViewpoints.Add(viewPoint);
                }
            }

            IsDriveable = true;
        }

        /// <summary>
        /// Parse the wag file parameters required for the simulator and viewer classes
        /// </summary>
        public override void Parse(string lowercasetoken, STFReader f)
        {
            switch (lowercasetoken)
            {
                case "engine(sound": CabSoundFileName = f.ReadStringBlock(); break;
                case "engine(cabview": CVFFileName = f.ReadStringBlock(); break;
                default: base.Parse(lowercasetoken, f); break;
            }
        }

        /// <summary>
        /// This initializer is called when we are making a new copy of a car already
        /// loaded in memory.  We use this one to speed up loading by eliminating the
        /// need to parse the wag file multiple times.
        /// </summary>
        public override void InitializeFromCopy(MSTSWagon copy)
        {
            MSTSLocomotive locoCopy = (MSTSLocomotive)copy;
            CabSoundFileName = locoCopy.CabSoundFileName;
            CVFFileName = locoCopy.CVFFileName;
            CVFFile = locoCopy.CVFFile;

            IsDriveable = copy.IsDriveable;

            base.InitializeFromCopy(copy);  // each derived level initializes its own variables
        }

        /// <summary>
        /// We are saving the game.  Save anything that we'll need to restore the 
        /// status later.
        /// </summary>
        public override void Save(BinaryWriter outf)
        {
            // we won't save the horn state
            outf.Write(Bell);
            outf.Write(Sander);
            outf.Write(Wiper);
            base.Save(outf);
        }

        /// <summary>
        /// We are restoring a saved game.  The TrainCar class has already
        /// been initialized.   Restore the game state.
        /// </summary>
        public override void Restore(BinaryReader inf)
        {
            if (inf.ReadBoolean()) SignalEvent(EventID.BellOn);
            if (inf.ReadBoolean()) SignalEvent(EventID.SanderOn);
            if (inf.ReadBoolean()) SignalEvent(EventID.WiperOn);
            base.Restore(inf);
        }



        /// <summary>
        /// Create a viewer for this locomotive.   Viewers are only attached
        /// while the locomotive is in viewing range.
        /// </summary>
        public override TrainCarViewer GetViewer(Viewer3D viewer)
        {
            return new MSTSLocomotiveViewer(viewer, this);
        }

        /// <summary>
        /// This is a periodic update to calculate physics 
        /// parameters and update the base class's MotiveForceN 
        /// and FrictionForceN values based on throttle settings
        /// etc for the locomotive.
        /// </summary>
        public override void Update(float elapsedClockSeconds)
        {
            // TODO  this is a wild simplification for electric and diesel electric
            float maxForceN = 300e3f * ThrottlePercent / 100f;   // TODO pull 300e3 from wag file
            float maxSpeedMpS = MpS.FromMpH(50) * ThrottlePercent / 100f;  // TODO pull 50 from wag file
            float currentSpeedMpS = Math.Abs(SpeedMpS);
            float balanceRatio = 1;
            if (maxSpeedMpS > currentSpeedMpS)
                balanceRatio = currentSpeedMpS / maxSpeedMpS;

            MotiveForceN = ( Direction == Direction.Forward ? 1 : -1) * maxForceN * (1f - balanceRatio);

            // Variable1 is wheel rotation in m/sec for steam locomotives
            Variable2 = Math.Abs(MotiveForceN) / 300e3f;   // force generated
            Variable1 = ThrottlePercent / 100f;   // throttle setting

            base.Update(elapsedClockSeconds);
        }

        public void SetDirection( Direction direction )
        {
            // Direction Control
            if ( Direction != direction)
            {
                Direction = direction;
                SignalEvent( direction == Direction.Forward ? EventID.Forward : EventID.Reverse);
            }
        }

        public void SetThrottle( float percent )
        {
            if (percent < 0) percent = 0;       // limit the range
            if (percent > 100) percent = 100;

            ThrottlePercent = percent;   
        }
        
        /// <summary>
        /// Used when someone want to notify us of an event
        /// </summary>
        public override void SignalEvent(EventID eventID)
        {
            switch (eventID)
            {
                case EventID.BellOn: Bell = true; break;
                case EventID.BellOff: Bell = false; break;
                case EventID.HornOn: Horn = true; break;
                case EventID.HornOff: Horn = false; break;
                case EventID.SanderOn: Sander = true; break;
                case EventID.SanderOff: Sander = false; break;
                case EventID.WiperOn: Wiper = true; break;
                case EventID.WiperOff: Wiper = false; break;
            }

            base.SignalEvent(eventID );
        }

    } // LocomotiveSimulator

    ///////////////////////////////////////////////////
    ///   3D VIEW
    ///////////////////////////////////////////////////

    /// <summary>
    /// Adds animation for wipers to the basic TrainCar
    /// </summary>
    public class MSTSLocomotiveViewer : MSTSWagonViewer
    {
        MSTSLocomotive Locomotive;

        List<int> WiperPartIndexes = new List<int>();

        float WiperAnimationKey = 0;

        protected MSTSLocomotive MSTSLocomotive { get { return (MSTSLocomotive)Car; } }

        public MSTSLocomotiveViewer(Viewer3D viewer, MSTSLocomotive car)
            : base(viewer, car)
        {
            Locomotive = car;


            // Find the animated parts
            if (TrainCarShape.SharedShape.Animations != null)
            {
                for (int iMatrix = 0; iMatrix < TrainCarShape.SharedShape.MatrixNames.Length; ++iMatrix)
                {
                    string matrixName = TrainCarShape.SharedShape.MatrixNames[iMatrix].ToUpper();
                    switch (matrixName)
                    {
                        case "WIPERARMLEFT1":
                        case "WIPERBLADELEFT1":
                        case "WIPERARMRIGHT1":
                        case "WIPERBLADERIGHT1":
                            if (TrainCarShape.SharedShape.Animations[0].FrameCount > 1)  // ensure shape file is properly animated for wipers
                                WiperPartIndexes.Add(iMatrix);
                            break;
                        case "MIRRORARMLEFT1":
                        case "MIRRORLEFT1":
                        case "MIRRORARMRIGHT1":
                        case "MIRRORRIGHT1":
                            // TODO
                            break;
                    }
                }
            }

            string wagonFolderSlash = Path.GetDirectoryName(Locomotive.WagFilePath) + "\\";
            if (Locomotive.CabSoundFileName != null) LoadCarSound(wagonFolderSlash, Locomotive.CabSoundFileName);

        }

        /// <summary>
        /// A keyboard or mouse click has occured. Read the UserInput
        /// structure to determine what was pressed.
        /// </summary>
        public override void HandleUserInput(ElapsedTime elapsedTime)
        {
            if (UserInput.IsPressed(Keys.W)) Locomotive.SetDirection(Direction.Forward);
            if (UserInput.IsPressed(Keys.S)) Locomotive.SetDirection(Direction.Reverse);
            if (UserInput.IsPressed(Keys.D)) Locomotive.SetThrottle( Locomotive.ThrottlePercent + 10);
            if (UserInput.IsPressed(Keys.A)) Locomotive.SetThrottle(Locomotive.ThrottlePercent - 10);
            if (UserInput.IsPressed(Keys.OemQuotes) || UserInput.IsPressed(Keys.E)) Locomotive.MSTSBrakeSystem.Increase();
            if (UserInput.IsPressed(Keys.OemSemicolon) || UserInput.IsPressed(Keys.Q)) Locomotive.MSTSBrakeSystem.Decrease();
            if (UserInput.IsPressed(Keys.X)) Locomotive.Train.SignalEvent(Locomotive.Sander ? EventID.SanderOff : EventID.SanderOn); 
            if (UserInput.IsPressed(Keys.V)) Locomotive.SignalEvent(Locomotive.Wiper ? EventID.WiperOff : EventID.WiperOn);
            if (UserInput.IsKeyDown(Keys.Space) != Locomotive.Horn) Locomotive.SignalEvent(Locomotive.Horn ? EventID.HornOff : EventID.HornOn);
            if (UserInput.IsAltKeyDown(Keys.B) != Locomotive.Bell) Locomotive.SignalEvent(Locomotive.Bell ? EventID.BellOff : EventID.BellOn);

            base.HandleUserInput( elapsedTime );
        }

        /// <summary>
        /// We are about to display a video frame.  Calculate positions for 
        /// animated objects, and add their primitives to the RenderFrame list.
        /// </summary>
        public override void PrepareFrame(RenderFrame frame, ElapsedTime elapsedTime)
        {
            float elapsedClockSeconds = elapsedTime.ClockSeconds;
            // Wiper animation
            if (WiperPartIndexes.Count > 0)  // skip this if there are no wipers
            {
                if (Locomotive.Wiper) // on
                {
                    // Wiper Animation
                    // Compute the animation key based on framerate etc
                    // ie, with 8 frames of animation, the key will advance from 0 to 8 at the specified speed.
                    WiperAnimationKey += ((float)TrainCarShape.SharedShape.Animations[0].FrameRate / 10f) * elapsedClockSeconds;
                    while (WiperAnimationKey >= TrainCarShape.SharedShape.Animations[0].FrameCount) WiperAnimationKey -= TrainCarShape.SharedShape.Animations[0].FrameCount;
                    while (WiperAnimationKey < -0.00001) WiperAnimationKey += TrainCarShape.SharedShape.Animations[0].FrameCount;
                    foreach (int iMatrix in WiperPartIndexes)
                        TrainCarShape.AnimateMatrix(iMatrix, WiperAnimationKey);
                }
                else // off
                {
                    if (WiperAnimationKey > 0.001)  // park the blades
                    {
                        WiperAnimationKey += ((float)TrainCarShape.SharedShape.Animations[0].FrameRate / 10f) * elapsedClockSeconds;
                        if (WiperAnimationKey >= TrainCarShape.SharedShape.Animations[0].FrameCount) WiperAnimationKey = 0;
                        foreach (int iMatrix in WiperPartIndexes)
                            TrainCarShape.AnimateMatrix(iMatrix, WiperAnimationKey);
                    }
                }
            }

            base.PrepareFrame( frame, elapsedTime );
        }


        /// <summary>
        /// This doesn't function yet.
        /// </summary>
        public override void Unload()
        {
            base.Unload();
        }

    } // Class LocomotiveViewer



}
