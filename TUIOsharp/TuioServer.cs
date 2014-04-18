/*
 * @author Valentin Simonov / http://va.lent.in/
 */

using System;
using System.Collections.Generic;
using OSCsharp.Data;
using OSCsharp.Net;
using OSCsharp.Utils;
using TUIOsharp;

namespace TUIOsharp
{
    public class TuioServer
    {
        #region Private vars

        private UDPReceiver udpReceiver;

        private Dictionary<int, TuioCursor> cursors = new Dictionary<int, TuioCursor>();

        private List<TuioCursor> updatedCursors = new List<TuioCursor>();
        private List<int> addedCursors = new List<int>();
        private List<int> removedCursors = new List<int>();

        private float movementThreshold = 0;
        private float movementThresholdSq = 0;
        private object objectSync = new object();
        private Dictionary<long, TuioObject> objectList = new Dictionary<long, TuioObject>(32);
        private List<long> aliveObjectList = new List<long>(32);
        private List<long> newObjectList = new List<long>(32);
        private List<TuioObject> frameObjects = new List<TuioObject>(32);
        private int currentFrame = 0;
        private TuioTime currentTime;
        private List<TuioListener> listenerList = new List<TuioListener>();


        #endregion

        #region Public properties

        public float MovementThreshold
        {
            get { return movementThreshold; }
            set
            {
                movementThreshold = value;
                movementThresholdSq = value * value;
            }
        }

        public int Port { get; private set; }
        public int FrameNumber { get; private set; }

        #endregion

        #region Events

        public event EventHandler<TuioCursorEventArgs> CursorAdded;
        public event EventHandler<TuioCursorEventArgs> CursorUpdated;
        public event EventHandler<TuioCursorEventArgs> CursorRemoved;

        public event EventHandler<TuioObjectEventArgs> ObjectAdded;
        public event EventHandler<TuioObjectEventArgs> ObjectUpdated;
        public event EventHandler<TuioObjectEventArgs> ObjectRemoved;

        public event EventHandler<ExceptionEventArgs> ErrorOccured;

        #endregion

        #region Constructors

        public TuioServer()
            : this(3333)
        { }

        public TuioServer(int port)
        {
            Port = port;

            MovementThreshold = 0;

            udpReceiver = new UDPReceiver(Port, false);
            udpReceiver.MessageReceived += handlerOscMessageReceived;
            udpReceiver.ErrorOccured += handlerOscErrorOccured;
        }

        #endregion

        #region Public methods

        public void Connect()
        {
            if (!udpReceiver.IsRunning) udpReceiver.Start();
        }

        public void Disconnect()
        {
            if (udpReceiver.IsRunning) udpReceiver.Stop();
        }

        #endregion

        #region Private functions

        private void parseOscMessage(OscMessage message)
        {
            switch (message.Address)
            {
                case "/tuio/2Dcur":
                    if (message.Data.Count == 0) return;
                    var command = message.Data[0].ToString();
                    switch (command)
                    {
                        case "set":
                            if (message.Data.Count < 4) return;
                            var id = (int)message.Data[1];
                            var xPos = (float)message.Data[2];
                            var yPos = (float)message.Data[3];
                            TuioCursor cursor;
                            if (!cursors.TryGetValue(id, out cursor))
                            {
                                cursor = new TuioCursor(id);
                            }
                            var deltaX = cursor.X - xPos;
                            var deltaY = cursor.Y - yPos;
                            if (deltaX * deltaX + deltaY * deltaY >= movementThresholdSq)
                            {
                                cursor.Update(xPos, yPos);
                                updatedCursors.Add(cursor);
                            }
                            break;
                        case "alive":
                            var aliveCursors = new List<int>();
                            for (var i = 1; i < message.Data.Count; i++)
                            {
                                aliveCursors.Add((int)message.Data[i]);
                            }
                            foreach (KeyValuePair<int, TuioCursor> value in cursors)
                            {
                                if (!aliveCursors.Contains(value.Key))
                                {
                                    removedCursors.Add(value.Key);
                                }
                                aliveCursors.Remove(value.Key);
                            }
                            addedCursors.AddRange(aliveCursors);
                            break;
                        case "fseq":
                            if (message.Data.Count < 2) return;
                            FrameNumber = (int)message.Data[1];
                            foreach (var updatedCursor in updatedCursors)
                            {
                                if (addedCursors.Contains(updatedCursor.Id) && !cursors.ContainsKey(updatedCursor.Id))
                                {
                                    cursors.Add(updatedCursor.Id, updatedCursor);
                                    if (CursorAdded != null) CursorAdded(this, new TuioCursorEventArgs(updatedCursor));
                                }
                                else
                                {
                                    if (CursorUpdated != null) CursorUpdated(this, new TuioCursorEventArgs(updatedCursor));
                                }
                            }
                            foreach (var cursorId in removedCursors)
                            {
                                cursor = cursors[cursorId];
                                cursors.Remove(cursorId);
                                if (CursorRemoved != null) CursorRemoved(this, new TuioCursorEventArgs(cursor));
                            }

                            addedCursors.Clear();
                            removedCursors.Clear();
                            updatedCursors.Clear();
                            break;
                    }
                    break;

                case "/tuio/2Dobj":
                    if (message.Data.Count == 0) return;
                     command = message.Data[0].ToString();

                     
                    if (command == "set")
                    {

                        long s_id = (int)message.Data[1];
                        int f_id = (int)message.Data[2];
                        float xpos = (float)message.Data[3];
                        float ypos = (float)message.Data[4];
                        float angle = (float)message.Data[5];
                        float xspeed = (float)message.Data[6];
                        float yspeed = (float)message.Data[7];
                        float rspeed = (float)message.Data[8];
                        float maccel = (float)message.Data[9];
                        float raccel = (float)message.Data[10];

                        lock (objectSync)
                        {
                            if (!objectList.ContainsKey(s_id))
                            {
                                TuioObject addObject = new TuioObject(s_id, f_id, xpos, ypos, angle);
                                frameObjects.Add(addObject);
                            }
                            else
                            {
                                TuioObject tobj = objectList[s_id];
                                if (tobj == null) return;
                                if ((tobj.getX() != xpos) || (tobj.getY() != ypos) || (tobj.getAngle() != angle) || (tobj.getXSpeed() != xspeed) || (tobj.getYSpeed() != yspeed) || (tobj.getRotationSpeed() != rspeed) || (tobj.getMotionAccel() != maccel) || (tobj.getRotationAccel() != raccel))
                                {

                                    TuioObject updateObject = new TuioObject(s_id, f_id, xpos, ypos, angle);
                                    updateObject.update(xpos, ypos, angle, xspeed, yspeed, rspeed, maccel, raccel);
                                    frameObjects.Add(updateObject);
                                }
                            }
                        }

                    }
                    else if (command == "alive")
                    {

                        newObjectList.Clear();
                        for (int i = 1; i < message.Data.Count; i++)
                        {
                            // get the message content
                            long s_id = (int)message.Data[i];
                            newObjectList.Add(s_id);
                            // reduce the object list to the lost objects
                            if (aliveObjectList.Contains(s_id))
                                aliveObjectList.Remove(s_id);
                        }

                        // remove the remaining objects
                        lock (objectSync)
                        {
                            for (int i = 0; i < aliveObjectList.Count; i++)
                            {
                                long s_id = aliveObjectList[i];
                                TuioObject removeObject = objectList[s_id];
                                removeObject.remove(currentTime);
                                frameObjects.Add(removeObject);
                            }
                        }

                    }
                    else if (command == "fseq")
                    {
                        int fseq = (int)message.Data[1];
                        bool lateFrame = false;

                        if (fseq > 0)
                        {
                            if (fseq > currentFrame) currentTime = TuioTime.getSessionTime();
                            if ((fseq >= currentFrame) || ((currentFrame - fseq) > 100)) currentFrame = fseq;
                            else lateFrame = true;
                        }
                        else if ((TuioTime.getSessionTime().getTotalMilliseconds() - currentTime.getTotalMilliseconds()) > 100)
                        {
                            currentTime = TuioTime.getSessionTime();
                        }

                        if (!lateFrame)
                        {

                            IEnumerator<TuioObject> frameEnum = frameObjects.GetEnumerator();
                            while (frameEnum.MoveNext())
                            {
                                TuioObject tobj = frameEnum.Current;

                                switch (tobj.getTuioState())
                                {
                                    case TuioObject.TUIO_REMOVED:
                                        TuioObject removeObject = tobj;
                                        removeObject.remove(currentTime);

                                        for (int i = 0; i < listenerList.Count; i++)
                                        {
                                            TuioListener listener = (TuioListener)listenerList[i];
                                            if (listener != null) listener.removeTuioObject(removeObject);
                                        }
                                        lock (objectSync)
                                        {
                                            objectList.Remove(removeObject.getSessionID());
                                        }
                                        if (ObjectRemoved != null) ObjectRemoved(this, new TuioObjectEventArgs(removeObject));
                                        break;
                                    case TuioObject.TUIO_ADDED:
                                        TuioObject addObject = new TuioObject(currentTime, tobj.getSessionID(), tobj.getSymbolID(), tobj.getX(), tobj.getY(), tobj.getAngle());
                                        lock (objectSync)
                                        {
                                            objectList.Add(addObject.getSessionID(), addObject);
                                        }
                                        for (int i = 0; i < listenerList.Count; i++)
                                        {
                                            TuioListener listener = (TuioListener)listenerList[i];
                                            if (listener != null) listener.addTuioObject(addObject);
                                        }
                                        
                                        if (ObjectAdded != null) ObjectAdded(this, new TuioObjectEventArgs(addObject));

                                        break;
                                    default:
                                        TuioObject updateObject = getTuioObject(tobj.getSessionID());
                                        if ((tobj.getX() != updateObject.getX() && tobj.getXSpeed() == 0) || (tobj.getY() != updateObject.getY() && tobj.getYSpeed() == 0))
                                            updateObject.update(currentTime, tobj.getX(), tobj.getY(), tobj.getAngle());
                                        else
                                            updateObject.update(currentTime, tobj.getX(), tobj.getY(), tobj.getAngle(), tobj.getXSpeed(), tobj.getYSpeed(), tobj.getRotationSpeed(), tobj.getMotionAccel(), tobj.getRotationAccel());

                                        for (int i = 0; i < listenerList.Count; i++)
                                        {
                                            TuioListener listener = (TuioListener)listenerList[i];
                                            if (listener != null) listener.updateTuioObject(updateObject);
                                        }
                                        if (ObjectUpdated != null) ObjectUpdated(this, new TuioObjectEventArgs(updateObject));

                                        break;
                                }
                            }

                            for (int i = 0; i < listenerList.Count; i++)
                            {
                                TuioListener listener = (TuioListener)listenerList[i];
                                if (listener != null) listener.refresh(new TuioTime(currentTime));
                            }

                            List<long> buffer = aliveObjectList;
                            aliveObjectList = newObjectList;
                            // recycling the List
                            newObjectList = buffer;
                        }
                        frameObjects.Clear();
                    }
                    break;
            }

        }
        /**
		 * Returns a Vector of all currently active TuioObjects
		 *
		 * @return a Vector of all currently active TuioObjects
		 */
        public List<TuioObject> getTuioObjects()
        {
            List<TuioObject> listBuffer;
            lock (objectSync)
            {
                listBuffer = new List<TuioObject>(objectList.Values);
            }
            return listBuffer;
        }
        public TuioObject getTuioObject(long s_id)
        {
            TuioObject tobject = null;
            lock (objectSync)
            {
                objectList.TryGetValue(s_id, out tobject);
            }
            return tobject;
        }

        public bool IsMarkerAlive(int in_MarkerID)
        {
            
            lock (objectSync)
            {
                foreach (TuioObject tuioObject in objectList.Values)
                {
                 
                    if (tuioObject.getSymbolID() == in_MarkerID)
                    {
                        return true;
                    }

                }
            }
            return false;
        }


        public TuioObject GetMarker(int in_MarkerID)
        {
            TuioObject tobject = null;
            lock (objectSync)
            {
                foreach (TuioObject tuioObject in objectList.Values)
                {
                    if (tuioObject.getSymbolID() == in_MarkerID)
                    {
                        return tuioObject;
                    }

                }
            }
            return tobject;
        }

        public int GetObjectCount()
        {
            return this.objectList.Count;
        }
        #endregion

        #region Event handlers

        private void handlerOscErrorOccured(object sender, ExceptionEventArgs exceptionEventArgs)
        {
            if (ErrorOccured != null) ErrorOccured(this, exceptionEventArgs);
        }

        private void handlerOscMessageReceived(object sender, OscMessageReceivedEventArgs oscMessageReceivedEventArgs)
        {
            parseOscMessage(oscMessageReceivedEventArgs.Message);
        }

        #endregion
    }

    public class TuioCursorEventArgs : EventArgs
    {
        public TuioCursor Cursor;

        public TuioCursorEventArgs(TuioCursor cursor)
        {
            Cursor = cursor;
        }
    }
    public class TuioObjectEventArgs : EventArgs
    {
        public TuioObject Object;

        public TuioObjectEventArgs(TuioObject obj)
        {
            Object = obj;
        }
    }
}