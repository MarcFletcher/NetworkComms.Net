//  Copyright 2009-2014 Marc Fletcher, Matthew Dean
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
//  Non-GPL versions of this software can also be purchased. 
//  Please see <http://www.networkcomms.net> for details.

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Reflection;
using System.Threading;

#if !NET2 && !NET35
using System.Threading.Tasks;
#endif

#if WINDOWS_PHONE || NETFX_CORE
using System.Linq;
using MarshalByRefObject = System.Object;
using System.Threading.Tasks;
#endif

namespace NetworkCommsDotNet.DPSBase
{
    /// <summary>
    /// Automatically detects and manages the use of <see cref="DataSerializer"/> and <see cref="DataProcessor"/>s.  
    /// Any <see cref="DataSerializer"/> or <see cref="DataProcessor"/> in an assembly located in the working 
    /// directory (including subdirectories) will be automatically detected.
    /// </summary>
    public sealed class DPSManager
    {
        #region Comparers
                
        class ReflectedTypeComparer : IEqualityComparer<Type>
        {
            public static ReflectedTypeComparer Instance { get; private set; }

            static ReflectedTypeComparer() { Instance = new ReflectedTypeComparer(); }

            public ReflectedTypeComparer() { }

            #region IEqualityComparer<ICompress> Members

            public bool Equals(Type x, Type y)
            {
                return x.AssemblyQualifiedName == y.AssemblyQualifiedName;
            }

            public int GetHashCode(Type obj)
            {
                return obj.AssemblyQualifiedName.GetHashCode();
            }

            #endregion
        }

        class AssemblyComparer : IEqualityComparer<AssemblyName>
        {
            public static AssemblyComparer Instance { get; private set; }

            static AssemblyComparer() { Instance = new AssemblyComparer(); }

            public AssemblyComparer() { }

            #region IEqualityComparer<AssemblyName> Members

            public bool Equals(AssemblyName x, AssemblyName y)
            {
                return x.FullName == y.FullName;               
            }

            public int GetHashCode(AssemblyName obj)
            {
                return obj.FullName.GetHashCode();
            }

            #endregion
        }

        #endregion

        private Dictionary<string, bool> AssembliesToLoad = new Dictionary<string, bool>();
        
        private Dictionary<string, DataSerializer> SerializersByType = new Dictionary<string, DataSerializer>();        
        private Dictionary<string, DataProcessor> DataProcessorsByType = new Dictionary<string, DataProcessor>();

        private Dictionary<byte, string> DataSerializerIdToType = new Dictionary<byte, string>();
        private Dictionary<byte, string> DataProcessorIdToType = new Dictionary<byte, string>();

        private object addRemoveObjectLocker = new object();

        private ManualResetEvent loadCompleted = new ManualResetEvent(false);

        static object instance = null;
        static object singletonLocker = new object();
        private static DPSManager Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (singletonLocker)
                    {
                        if (instance == null)
                        {
                            instance = new DPSManager();
                        }
                    }
                }

                return instance as DPSManager;
            }
        }
                        
        /// <summary>
        /// Retrieves the singleton instance of the <see cref="DataSerializer"/> with <see cref="System.Type"/> T
        /// </summary>
        /// <typeparam name="T">The <see cref="System.Type"/> of the <see cref="DataSerializer"/> to retrieve </typeparam>
        /// <returns>The retrieved singleton instance of the desired <see cref="DataSerializer"/></returns>
        public static DataSerializer GetDataSerializer<T>() where T : DataSerializer
        {
            if (!Instance.SerializersByType.ContainsKey(typeof(T).AssemblyQualifiedName))
            {
                //Make the serializer
                var serializer = CreateObjectWithParameterlessCtor(typeof(T).AssemblyQualifiedName) as DataSerializer;
                //Get the attribute value
                var attr = serializer.Identifier;

                lock (Instance.addRemoveObjectLocker)
                {
                    if (!Instance.SerializersByType.ContainsKey(typeof(T).AssemblyQualifiedName))
                    {
                        Instance.SerializersByType.Add(typeof(T).AssemblyQualifiedName, serializer);
                        Instance.DataSerializerIdToType.Add(attr, typeof(T).AssemblyQualifiedName);
                    }
                }
            }

            return GetDataSerializer(typeof(T).AssemblyQualifiedName);
        }

        /// <summary>
        /// Retrieves the singleton instance of the <see cref="DataSerializer"/> corresponding to a given id
        /// </summary>
        /// <param name="Id">The identifier corresponding to the desired <see cref="DataSerializer"/></param>
        /// <returns>The retrieved singleton instance of the desired <see cref="DataSerializer"/></returns>
        public static DataSerializer GetDataSerializer(byte Id)
        {
            if (Instance.DataSerializerIdToType.ContainsKey(Id))
                return GetDataSerializer(Instance.DataSerializerIdToType[Id]);
            else
            {
                //if we're after protobuf we'll try and load it manually
                if (Id == 1 && !Instance.loadCompleted.WaitOne(0))
                {
                    Type t = null;

                    t = Type.GetType("NetworkCommsDotNet.DPSBase.ProtobufSerializer");

                    if (t == null)
                    {
                        try
                        {
                            AssemblyName assName = new AssemblyName("ProtobufSerializer");
                            Assembly protoAss = Assembly.Load(assName);
                            t = protoAss.GetType("NetworkCommsDotNet.DPSBase.ProtobufSerializer");
                        }
                        catch (Exception) { }
                    }                    

                    if (t != null)
                    {
                        //Make a protobuf serializer
                        DataSerializer serializer = CreateObjectWithParameterlessCtor(t.AssemblyQualifiedName) as DataSerializer;
                        //Get the id
                        var attr = serializer.Identifier;

                        lock (Instance.addRemoveObjectLocker)
                        {
                            if (!Instance.SerializersByType.ContainsKey(t.AssemblyQualifiedName))
                            {
                                Instance.SerializersByType.Add(t.AssemblyQualifiedName, serializer);
                                Instance.DataSerializerIdToType.Add(attr, t.AssemblyQualifiedName);
                                return serializer;
                            }
                        }
                    }
                }

                //if the id is not present we're going to have to wait for the dynamic load to finish
                Instance.loadCompleted.WaitOne();

                //Then we can try again
                if (Instance.DataSerializerIdToType.ContainsKey(Id))
                    return GetDataSerializer(Instance.DataSerializerIdToType[Id]);
                else
                    return null;
            }
        }

        private static DataSerializer GetDataSerializer(string t)
        {
            var serializer = Instance.SerializersByType[t];

            if (serializer == null)
            {
                lock (Instance.addRemoveObjectLocker)
                {
                    if (serializer == null)
                    {
                        serializer = CreateObjectWithParameterlessCtor(t) as DataSerializer;
                        Instance.SerializersByType[t] = serializer;
                    }
                }
            }

            return serializer;            
        }
                
        /// <summary>
        /// Retrieves the singleton instance of the <see cref="DataProcessor"/> with <see cref="System.Type"/> T
        /// </summary>
        /// <typeparam name="T">The <see cref="System.Type"/> of the <see cref="DataProcessor"/> to retrieve </typeparam>
        /// <returns>The retrieved singleton instance of the desired <see cref="DataProcessor"/></returns>
        public static DataProcessor GetDataProcessor<T>() where T : DataProcessor
        {
            if (!Instance.DataProcessorsByType.ContainsKey(typeof(T).AssemblyQualifiedName))
            {
                //Make the serializer
                var processor = CreateObjectWithParameterlessCtor(typeof(T).AssemblyQualifiedName) as DataProcessor;
                //Get the attribute value
                var attr = processor.Identifier;

                lock (Instance.addRemoveObjectLocker)
                {
                    if (!Instance.DataProcessorsByType.ContainsKey(typeof(T).AssemblyQualifiedName))
                    {
                        Instance.DataProcessorsByType.Add(typeof(T).AssemblyQualifiedName, processor);
                        Instance.DataProcessorIdToType.Add(attr, typeof(T).AssemblyQualifiedName);
                    }
                }
            }

            return GetDataProcessor(typeof(T).AssemblyQualifiedName);
        }

        /// <summary>
        /// Retrieves the singleton instance of the <see cref="DataProcessor"/> corresponding to a given id
        /// </summary>
        /// <param name="Id">The identifier corresponding to the desired <see cref="DataProcessor"/></param>
        /// <returns>The retrieved singleton instance of the desired <see cref="DataProcessor"/></returns>
        public static DataProcessor GetDataProcessor(byte Id)
        {
            if (Instance.DataProcessorIdToType.ContainsKey(Id))
                return GetDataProcessor(Instance.DataProcessorIdToType[Id]);
            else
            {
                //if the id is not present we're going to have to wait for the dynamic load to finish
                Instance.loadCompleted.WaitOne();

                //Then we can try again
                if (Instance.DataProcessorIdToType.ContainsKey(Id))
                    return GetDataProcessor(Instance.DataProcessorIdToType[Id]);
                else
                    return null;
            }
        }

        private static DataProcessor GetDataProcessor(string t)
        {
            var processor = Instance.DataProcessorsByType[t];

            if (processor == null)
            {
                processor = CreateObjectWithParameterlessCtor(t) as DataProcessor;
                Instance.DataProcessorsByType[t] = processor;
            }

            return processor;
        }

        /// <summary>
        /// Allows the addition of <see cref="DataProcessor"/>s which are not auto detected.  Use only if the assembly 
        /// in which the <see cref="DataProcessor"/> is defined is not in the working directory (including subfolders) 
        /// or if automatic detection is not supported on your platform.
        /// </summary>
        /// <param name="dataProcessor">The <see cref="DataProcessor"/> to make the <see cref="DPSManager"/> aware of</param>
        /// <exception cref="ArgumentException">Thrown if A different <see cref="DataProcessor"/> of the same 
        /// <see cref="System.Type"/> or Id has already been added to the <see cref="DPSManager"/></exception>
        public static void AddDataProcessor(DataProcessor dataProcessor)
        {
            if (dataProcessor == null) throw new ArgumentNullException("dataProcessor");

            lock (Instance.addRemoveObjectLocker)
            {
                if (Instance.DataProcessorsByType.ContainsKey(dataProcessor.GetType().AssemblyQualifiedName))
                    if (Instance.DataProcessorsByType[dataProcessor.GetType().AssemblyQualifiedName] != dataProcessor)
                        throw new ArgumentException("A different DataProcessor of the same Type or Id has already been added to DPSManager");
                    else
                        return;

                Instance.DataProcessorsByType.Add(dataProcessor.GetType().AssemblyQualifiedName, dataProcessor);
                Instance.DataProcessorIdToType.Add(dataProcessor.Identifier, dataProcessor.GetType().AssemblyQualifiedName);
            }
        }

        /// <summary>
        /// Allows the addition of <see cref="DataSerializer"/>s which are not auto detected.  Use only if the assembly 
        /// in which the <see cref="DataSerializer"/> is defined is not in the working directory (including subfolders) 
        /// or if automatic detection is not supported on your platform
        /// </summary>
        /// <param name="dataSerializer">The <see cref="DataSerializer"/> to make the see <see cref="DPSManager"/> aware of</param>
        /// <exception cref="ArgumentException">Thrown if A different <see cref="DataSerializer"/> of the same 
        /// <see cref="System.Type"/> or Id has already been added to the <see cref="DPSManager"/></exception>
        public static void AddDataSerializer(DataSerializer dataSerializer)
        {
            if (dataSerializer == null) throw new ArgumentNullException("dataSerializer");

            lock (Instance.addRemoveObjectLocker)
            {
                if (Instance.SerializersByType.ContainsKey(dataSerializer.GetType().AssemblyQualifiedName))
                    if (Instance.SerializersByType[dataSerializer.GetType().AssemblyQualifiedName] != dataSerializer)
                        throw new ArgumentException("A different DataSerializer of the same Type or Id has already been added to DPSManager");
                    else
                        return;

                Instance.SerializersByType.Add(dataSerializer.GetType().AssemblyQualifiedName, dataSerializer);
                Instance.DataSerializerIdToType.Add(dataSerializer.Identifier, dataSerializer.GetType().AssemblyQualifiedName);
            }
        }

        /// <summary>
        /// Generates an <see cref="long"/> describing a <see cref="DataSerializer"/> and a set of <see cref="DataProcessor"/>s
        /// </summary>
        /// <param name="serializer">The <see cref="DataSerializer"/> to be used</param>
        /// <param name="dataProcessors">A <see cref="System.Collections.Generic.List{DataProcessor}()"/> to be used.  The order of this </param>
        /// <returns>A <see cref="long"/> describing the arguments</returns>
        /// <exception cref="ArgumentException">Thrown is more than 7 <see cref="DataSerializer"/>s are used</exception>
        /// <remarks>This method is used to specify succinctly the serialization method and any data processing that will be 
        /// used when transmitting data using NetworkCommsDotNet</remarks>
        public static long CreateSerializerDataProcessorIdentifier(DataSerializer serializer, List<DataProcessor> dataProcessors)
        {
            if (serializer == null) throw new ArgumentNullException("serializer");

            long res = 0;

            res |= serializer.Identifier;
            res <<= 8;

            if (dataProcessors != null && dataProcessors.Count != 0)
            {
                if (dataProcessors.Count > 7)
                    throw new ArgumentException("Cannot specify more than 7 data processors for automatic serialization detection");

                for (int i = 0; i < dataProcessors.Count; i++)
                {
                    res |= dataProcessors[i].Identifier;

                    if (i != dataProcessors.Count - 1)
                        res <<= 8;
                }

                if (dataProcessors.Count < sizeof(long) - 1)
                    res <<= (8 * (sizeof(long) - 1 - dataProcessors.Count));
            }
            else
                res <<= (8 * (sizeof(long) - 2));

            return res;
        }

        /// <summary>
        /// Takes an identifier generated using <see cref="DPSManager.CreateSerializerDataProcessorIdentifier"/> and returns 
        /// the <see cref="DataSerializer"/> and set of <see cref="DataProcessor"/>s used to generate the identifier
        /// </summary>
        /// <param name="id">The <see cref="long"/> describing the <see cref="DataSerializer"/> and a set of <see cref="DataProcessor"/>s</param>
        /// <param name="serializer">The resultant <see cref="DataSerializer"/></param>
        /// <param name="dataProcessors">A List of the resultant <see cref="DataProcessor"/>s</param>
        /// <remarks>This method is used to extract the serialization method and any data processing that needs to 
        /// be used when transmitting data using NetworkCommsDotNet</remarks>
        public static void GetSerializerDataProcessorsFromIdentifier(long id, out DataSerializer serializer, out List<DataProcessor> dataProcessors)
        {
            byte serializerId = (byte)(id >> 56);
            serializer = GetDataSerializer(serializerId);
            if (serializer == null)
                throw new SerialisationException("Unable to locate a serializer with id=" + serializerId.ToString() + ". Please ensure the desired serializer is available and try again.");

            dataProcessors = new List<DataProcessor>();

            for (int i = 6; i >= 0; i--)
            {
                long mask = 0xFF;
                byte processorId = (byte)((id & (mask << (8 * i))) >> (8 * i));

                if (processorId != 0)
                {
                    DataProcessor selectedProcessor = GetDataProcessor(processorId);

                    if (selectedProcessor == null)
                        throw new SerialisationException("Unable to locate a data processor with id=" + processorId.ToString() + ". Please ensure the desired data processor is available and try again.");

                    dataProcessors.Add(selectedProcessor);
                }
            }
        }

        private static object CreateObjectWithParameterlessCtor(string typeName)
        {
#if NETFX_CORE
            var constructor = (from ctor in Type.GetType(typeName).GetTypeInfo().DeclaredConstructors
                                   where ctor.GetParameters().Length == 0
                                   select ctor).FirstOrDefault();
#else
            var typeToCreate = Type.GetType(typeName);

            var constructor = typeToCreate.GetConstructor(BindingFlags.Instance, null, new Type[] { }, null);

            if (constructor == null)
                constructor = typeToCreate.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { }, null);
#endif
            return constructor.Invoke(null);
        }

#if NET2
        private delegate void Action();
#endif

        private DPSManager()
        {
            //This constructor loops through referenced assemblies looking for types that inherit off of DataSerializer and 
            //DataProcessor.  On windows this should mean perfect auto detection of serializers and compressors. On windows 
            //phone we cannot get a list of referenced assemblies so we can only do this for already loaded assemblies.  
            //Any others that are used will have to be added manually.  On windows this will be done from a new app domain 
            //so we can unload it afterwards
            
            //This action will perform the load in the background on some client dependent "thread" 
            Action loadAction = new Action(() =>
                {
                    //Initialise the core extensions
                    DPSManager.GetDataSerializer<ExplicitSerializer>();
                    DPSManager.GetDataSerializer<NullSerializer>();
                    DPSManager.GetDataProcessor<DataPadder>();
#if !FREETRIAL
                    //Only the full version includes the encrypter
                    DPSManager.GetDataProcessor<RijndaelPSKEncrypter>();
#endif

#if !WINDOWS_PHONE && !NETFX_CORE
                    DPSManager.GetDataSerializer<BinaryFormaterSerializer>();
#endif

                    AssemblyLoader loader;
                    ProcessArgument args;

#if !WINDOWS_PHONE && !iOS && !ANDROID && !NETFX_CORE

                    AppDomain tempDomain = null;

                    try
                    {
                        //Create a new domain with the same settings as the current domain
                        AppDomainSetup setup = AppDomain.CurrentDomain.SetupInformation;
                        tempDomain = AppDomain.CreateDomain("Temp_" + Guid.NewGuid().ToString(), AppDomain.CurrentDomain.Evidence, setup);

                        try
                        {
                            //First try creating the proxy from the assembly using the assembly name
                            loader = (AssemblyLoader)tempDomain.CreateInstanceFromAndUnwrap(typeof(AssemblyLoader).Assembly.FullName, typeof(AssemblyLoader).FullName);
                        }
                        catch (FileNotFoundException)
                        {
                            //If that fails try with the assembly location.  An exception here 
                            loader = (AssemblyLoader)tempDomain.CreateInstanceFromAndUnwrap(typeof(AssemblyLoader).Assembly.Location, typeof(AssemblyLoader).FullName);
                        }

                        args = new ProcessArgument();

                        //If an entry assembly exists just pass that, the rest can be worked out from there.  
                        //On WCF there is no entry assembly. In that case fill the loaded domains list with those already loaded
                        if (Assembly.GetEntryAssembly() != null)
                            args.loadedDomains = new List<string>() { Assembly.GetEntryAssembly().FullName };
                        else
                        {
                            List<string> loadedDomains = new List<string>();

                            foreach (var ass in AppDomain.CurrentDomain.GetAssemblies())
                                loadedDomains.Add(ass.FullName);

                            args.loadedDomains = loadedDomains;
                        }

                        loader.ProcessApplicationAssemblies(args);
                    }
                    catch (FileNotFoundException)
                    {
                        //In mono, using mkbundle, the above load method may not work so we will fall back to our older way of doing the same
                        //The disadvantage of this approach is that all assemblies are loaded and then stay in memory increasing the footprint slightly 
                        loader = new AssemblyLoader();
                        args = new ProcessArgument();

                        loader.ProcessApplicationAssemblies(args);
                    }
                    catch (MissingMethodException)
                    {
                        loader = new AssemblyLoader();
                        args = new ProcessArgument();

                        loader.ProcessApplicationAssemblies(args);
                    }
                    catch (Exception)
                    {
                        loader = new AssemblyLoader();
                        args = new ProcessArgument();

                        loader.ProcessApplicationAssemblies(args);
                    }
                    finally
                    {
                        if (tempDomain != null)
                        {
                            try
                            {
                                AppDomain.Unload(tempDomain);
                            }
                            catch (Exception) { }
                            finally
                            {
                                tempDomain = null;
                                GC.Collect();
                            }
                        }
                    }
#else
                    loader = new AssemblyLoader();
                    args = new ProcessArgument();

                    loader.ProcessApplicationAssemblies(args);
#endif
                    foreach (var serializer in args.serializerTypes)
                    {
                        lock (addRemoveObjectLocker)
                        {
                            if (!SerializersByType.ContainsKey(serializer.Value))
                            {
                                SerializersByType.Add(serializer.Value, null);
                                DataSerializerIdToType.Add(serializer.Key, serializer.Value);
                            }
                        }
                    }

                    foreach (var processor in args.processorTypes)
                    {
                        lock (addRemoveObjectLocker)
                        {
                            if (!DataProcessorsByType.ContainsKey(processor.Value))
                            {
                                DataProcessorsByType.Add(processor.Value, null);
                                DataProcessorIdToType.Add(processor.Key, processor.Value);
                            }
                        }
                    }

                    loadCompleted.Set();
                });

#if NET2 || NET35
            Thread loadThread = new Thread(new ThreadStart(loadAction));
            loadThread.Name = "DPS load thread";
            loadThread.Start();
#else
            Task.Factory.StartNew(loadAction);
#endif

        }

        private class ProcessArgument : MarshalByRefObject
        {
#if !WINDOWS_PHONE  && !iOS && !ANDROID && !NETFX_CORE
            public List<string> loadedDomains;
#endif
            public Dictionary<byte, string> serializerTypes;// = new Dictionary<byte, string>();
            public Dictionary<byte, string> processorTypes;// = new Dictionary<byte, string>();
        }

        private class AssemblyLoader : MarshalByRefObject
        {
            public Dictionary<byte, string> serializerTypes = new Dictionary<byte, string>();
            public Dictionary<byte, string> processorTypes = new Dictionary<byte,string>();

            public void ProcessApplicationAssemblies(ProcessArgument args)
            {
                try
                {

#if !WINDOWS_PHONE && !iOS && !ANDROID && !NETFX_CORE
                    AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += CurrentDomain_ReflectionOnlyAssemblyResolve;

                    if (args.loadedDomains != null)
                    {
                        foreach (var domain in args.loadedDomains)
                        {
                            try
                            {
                                AppDomain.CurrentDomain.Load(domain);
                            }
                            catch (FileNotFoundException) { }
                        }
                    }
#endif

                    //Store the serializer and processor types as we will need then repeatedly
                    var serializerType = typeof(DPSBase.DataSerializer);
                    var processorType = typeof(DPSBase.DataProcessor);

                    //We're now going to look through the assemly reference tree to look for more components
                    //This will be done by first checking whether a relefection only load of each assembly and checking 
                    //for reference to DPSBase.  We will therefore get a reference to DPSBase
#if NETFX_CORE
                    var dpsBaseAssembly = typeof(DPSManager).GetTypeInfo().Assembly;
#else
                    var dpsBaseAssembly = typeof(DPSManager).Assembly;
#endif

#if NETFX_CORE
                    var folder = Windows.ApplicationModel.Package.Current.InstalledLocation;

                    List<Assembly> alreadyLoadedAssemblies = new List<Assembly>();

                    Func<Task> getAssemblies = new Func<Task>(async ()=>
                        {
                            var t = folder.GetFilesAsync().AsTask();
                            await t.ConfigureAwait(false);
                            t.Wait();
                            var filesInt = t.Result;

                            foreach (Windows.Storage.StorageFile file in filesInt)
                            {
                                if (file.FileType == ".dll" || file.FileType == ".exe")
                                {                                    
                                    AssemblyName name = new AssemblyName() { Name = file.Name.Substring(0, file.Name.Length - 4) };                                    
                                    Assembly asm = Assembly.Load(name);
                                    alreadyLoadedAssemblies.Add(asm);
                                }
                            }
                        });

                    getAssemblies().Wait();
#else
                    //Loop through all loaded assemblies looking for types that are not abstract and implement DataProcessor or DataSerializer.  They also need to have a paramterless contstructor                
                    var alreadyLoadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
#endif

                    //We are also going to keep a track of all assemblies with which we have considered types within
                    var dicOfSearchedAssemblies = new Dictionary<string, Assembly>();

                    //And all the assembly names we have tried to load
                    var listofConsideredAssemblies = new List<string>();

                    foreach (var ass in alreadyLoadedAssemblies)
                    {
#if NETFX_CORE
                        foreach (var type in ass.DefinedTypes)
                        {
                            byte id;
                            var attributes = type.GetCustomAttributes(typeof(DataSerializerProcessorAttribute), false);

                            if (attributes.Count() == 1)
                            {
                                id = (attributes.First() as DataSerializerProcessorAttribute).Identifier;
                            }
                            else
                                continue;

                            var constructor = (from ctor in type.DeclaredConstructors
                                               where ctor.GetParameters().Length == 0
                                               select ctor).FirstOrDefault();

                            if (serializerType.GetTypeInfo().IsAssignableFrom(type) && !type.IsAbstract && constructor != null)
                                serializerTypes.Add(id, type.AssemblyQualifiedName);

                            if (processorType.GetTypeInfo().IsAssignableFrom(type) && !type.IsAbstract && constructor != null)
                                processorTypes.Add(id, type.AssemblyQualifiedName);
                        }
#else
#if WINDOWS_PHONE || iOS || ANDROID
#else
                        foreach (var refAss in ass.GetReferencedAssemblies())
                        {
                            if (AssemblyComparer.Instance.Equals(dpsBaseAssembly.GetName(), refAss) || ass == dpsBaseAssembly)
                            {
#endif
                                foreach (var type in ass.GetTypes())
                                {
                                    byte id;
                                    var attributes = type.GetCustomAttributes(typeof(DataSerializerProcessorAttribute), false);

                                    if (attributes.Length == 1)
                                    {
                                        id = (attributes[0] as DataSerializerProcessorAttribute).Identifier;
                                    }
                                    else
                                        continue;

                                    if (serializerType.IsAssignableFrom(type) && !type.IsAbstract &&
                                        (type.GetConstructors(BindingFlags.Instance).Length != 0 || type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Length != 0))
                                    {
                                        //SerializersByType.Add(type, null);
                                        //DataSerializerIdToType.Add(id, type);
                                        serializerTypes.Add(id, type.AssemblyQualifiedName);
                                    }

                                    if (processorType.IsAssignableFrom(type) && !type.IsAbstract &&
                                        (type.GetConstructors(BindingFlags.Instance).Length != 0 || type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Length != 0))
                                    {
                                        //DataProcessorsByType.Add(type, null);
                                        //DataProcessorIdToType.Add(id, type);
                                        processorTypes.Add(id, type.AssemblyQualifiedName);
                                    }
                                }
#endif
#if WINDOWS_PHONE || iOS || ANDROID || NETFX_CORE
#else
                                break;
                            }
                        }
#endif
                                dicOfSearchedAssemblies.Add(ass.FullName, ass);
                    }

#if WINDOWS_PHONE || iOS || ANDROID || NETFX_CORE
#else
                //Set an identifier to come back to as we load assemblies
                AssemblySearchStart:

                    //Loop through all assemblies
                    foreach (var pair in dicOfSearchedAssemblies)
                    {
                        var assembly = pair.Value;

                        //Loop through the assemblies this assemlby references
                        foreach (var referencedAssembly in assembly.GetReferencedAssemblies())
                        {
                            //If we've already tried this assembly name then keep going.  Otherwise record that we will have tried this assembly
                            if (listofConsideredAssemblies.Contains(referencedAssembly.FullName))
                                continue;
                            else
                                listofConsideredAssemblies.Add(referencedAssembly.FullName);

                            Assembly refAssembly = null;

                            //Occationally assemblies will not resolve (f**k knows why).  They will then throw a FileNotFoundException that we can catch and ignore
                            try
                            {
                                //Do a reflection only load of the assembly so that we can see if it references DPSBase and also what it does reference
                                refAssembly = System.Reflection.Assembly.ReflectionOnlyLoad(referencedAssembly.FullName);
                            }
                            catch (FileNotFoundException)
                            { continue; }

                            //Note that multiple assembly names/versions could resolve to this assembly so check if we're already considered the actual
                            //loaded assembly
                            if (!dicOfSearchedAssemblies.ContainsKey(refAssembly.FullName))
                            {
                                //if not add it to the considered list
                                dicOfSearchedAssemblies.Add(refAssembly.FullName, refAssembly);

                                //if it references DPSBase directly it might contain components. Add the assembly to the catalog
                                foreach (var refAss in refAssembly.GetReferencedAssemblies())
                                {
                                    if (AssemblyComparer.Instance.Equals(dpsBaseAssembly.GetName(), refAss))
                                    {
                                        foreach (var type in refAssembly.GetTypes())
                                        {
                                            bool idSet = false;
                                            byte id = 0;
                                            //var attributes = type.GetCustomAttributes(typeof(DataSerializerProcessorAttribute), false);
                                            var attributes = CustomAttributeData.GetCustomAttributes(type);

                                            foreach (var attr in attributes)
                                            {
                                                if (attr.Constructor.ReflectedType.AssemblyQualifiedName == typeof(DataSerializerProcessorAttribute).AssemblyQualifiedName)
                                                {
                                                    id = (byte)attr.ConstructorArguments[0].Value;
                                                    idSet = true;
                                                }
                                            }

                                            if (!idSet)
                                                continue;

                                            Type baseType = type.BaseType;

                                            while (baseType != null)
                                            {
                                                if (baseType.AssemblyQualifiedName == serializerType.AssemblyQualifiedName)
                                                {
                                                    //SerializersByType.Add(type, null);
                                                    //DataSerializerIdToType.Add(id, type);
                                                    serializerTypes.Add(id, type.AssemblyQualifiedName);
                                                    break;
                                                }

                                                if (baseType.AssemblyQualifiedName == processorType.AssemblyQualifiedName)
                                                {
                                                    //DataProcessorsByType.Add(type, null);
                                                    //DataProcessorIdToType.Add(id, type);
                                                    processorTypes.Add(id, type.AssemblyQualifiedName);
                                                    break;
                                                }

                                                baseType = baseType.BaseType;
                                            }
                                        }

                                        break;
                                    }
                                }

                                //We're changed allAssemblies and loadedAssemblies so restart
                                goto AssemblySearchStart;
                            }
                        }
                    }
#endif
                }
                catch (Exception)
                {
                    //int i = 1;

                    //using (StreamWriter sw = new StreamWriter("DPSManagerLoadError.txt", false))
                        //Console.WriteLine(ex.ToString());
                }
                finally
                {
                    args.processorTypes = processorTypes;
                    args.serializerTypes = serializerTypes;
                }
            }

#if !WINDOWS_PHONE && !iOS && !ANDROID && !NETFX_CORE
            Assembly CurrentDomain_ReflectionOnlyAssemblyResolve(object sender, ResolveEventArgs args)
            {
                return Assembly.ReflectionOnlyLoad(args.Name); 
            }
#endif
        }
    }
}
