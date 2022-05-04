using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Video;

public class IR_IBLReplayTask : Experiment
{
    private Utils util;
    private Transform wheelTransform;
    private AudioManager audmanager;
    private LickBehavior lickBehavior;
    private ExperimentManager emanager;
    private VisualStimulusManager vsmanager;
    private NeuronEntityManager nemanager;
    private IR_IBLReplayManager replayManager;

    private Dictionary<string, Dictionary<string, string>> sessionURIs;
    private Dictionary<string, Dictionary<int,int[]>> sessionCluUUIDIdxs; // converts from clu to uuid index
    private Dictionary<string, Dictionary<string, Array>> sessionData;
    private Dictionary<string, Dictionary<int, Vector3[]>> trajectoryData;

    private GameObject _uiPanel;
    private TMP_Dropdown uiDropdown;

    private bool dataLoaded = false;

    private Dictionary<int, Entity> neurons;
    List<Dictionary<string, object>> mlapdv;
    List<Dictionary<string, object>> trajectories;

    // PROBES
    private List<Transform> tips;

    // STIMULUS
    private GameObject stimL;
    private GameObject stimR;
    private float stimAz = 20;
    private Vector2 stimAzMinMax = new Vector2(0, 40);
    private bool stimFrozen;
    private float rad2deg = 180 / Mathf.PI;
    private float rad2mm2deg = (196 / 2) / Mathf.PI * 4; // wheel circumference in mm * 4 deg / mm
    private Vector2 stimPosDeg; // .x = left, .y = right

    private float taskTime;

    // Local accessors
    private string cEID;
    private List<int> probes;
    private int[] si; // spike/cluster index
    private Vector2 wheelTime;
    private Vector2 wheelPos; // current wheel pos and next wheel pos
    private int wi; // wheel index
    private int gi; // go cue index
    private int fi; // feedback index
    private int li; // lick index
    private bool videoPlaying;
    VideoPlayer[] videos;


    // OTHER
    const float scale = 1000;
    private SpikingComponent spikedComponent;

    public IR_IBLReplayTask(Utils util, Transform wheelTransform, 
        AudioManager audmanager, LickBehavior lickBehavior, 
        VisualStimulusManager vsmanager, NeuronEntityManager nemanager,
        List<Transform> probeTips) : base("replay")
    {
        this.util = util;
        this.wheelTransform = wheelTransform;
        this.audmanager = audmanager;
        this.lickBehavior = lickBehavior;
        this.vsmanager = vsmanager;
        this.nemanager = nemanager;
        this.tips = probeTips;

        // Setup variables
        emanager = GameObject.Find("main").GetComponent<ExperimentManager>();
        spikedComponent = new SpikingComponent { spiking = 1f };

        // Setup task info
        LoadTaskInfo();
        
        // Populate panel
        List<TMP_Dropdown.OptionData> eids = new List<TMP_Dropdown.OptionData>();
        foreach (string eid in sessionURIs.Keys)
        {
            eids.Add(new TMP_Dropdown.OptionData(eid));
        }
        uiDropdown.options = eids;
        //uiDropdown.onValueChanged.AddListener(delegate { LoadDropdownEID(); });

        // Load CCF coordinates
        mlapdv = CSVReader.ReadFromResources("Datasets/ibl/ccf_mlapdv");

        _uiPanel.SetActive(false);

    }

    public void UpdateTime()
    {
        float seconds = TaskTime();

        float displayMilliseconds = (seconds % 1) * 1000;
        float displaySeconds = seconds % 60;
        float displayMinutes = (seconds / 60) % 60;
        float displayHours = (seconds / 3600) % 24;

        //private void UpdateTime()
        //{
        //    TimeSpan timeSpan = TimeSpan.FromSeconds(sessionCurrentTime);
        //    string timeText = string.Format("{0:D2}h.{1:D2}m.{2:D2}.{3:D3}", timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds, timeSpan.Milliseconds);
        //    timerText.SetText(timeText);
        //}
        GameObject replayText = GameObject.Find("Replay_Time");
        if (replayText)
            replayText.GetComponent<TextMeshProUGUI>().text = string.Format("{0:00}h:{1:00}m:{2:00}.{3:000}", displayHours, displayMinutes, displaySeconds, displayMilliseconds);
    }

    // Load task info sets everything up
    private void LoadTaskInfo()
    {
        sessionURIs = new Dictionary<string, Dictionary<string, string>>();
        sessionData = new Dictionary<string, Dictionary<string, Array>>();
        sessionCluUUIDIdxs = new Dictionary<string, Dictionary<int, int[]>>();

        UnityEngine.Object[] objects = Resources.LoadAll("FlatironPaths");
        foreach (UnityEngine.Object obj in objects)
        {
            ParseTaskInfo(obj);
        }

        // Load the probe trajectory CSV
        trajectories = CSVReader.ReadFromResources("Datasets/ibl/probe_trajectories");

        ParseTrajectoryInfo();
    }

    private void ParseTrajectoryInfo()
    {
        //private Dictionary<string, Dictionary<int, Vector3[]>> trajectoryData;
        trajectoryData = new Dictionary<string, Dictionary<int, Vector3[]>>();

        for (int i = 0; i < trajectories.Count; i++)
        {
            Dictionary<string, object> row = trajectories[i];

            string eid = (string)row["eid"];
            int probe = (int)char.GetNumericValue(((string)row["probe"])[6]);

            float ml = Convert.ToSingle(row["ml"]);
            float ap = Convert.ToSingle(row["ap"]);
            float dv = Convert.ToSingle(row["dv"]);
            float depth = Convert.ToSingle(row["depth"]);
            float theta = Convert.ToSingle(row["theta"]);
            float phi = Convert.ToSingle(row["phi"]);

            Vector3 mlapdv = new Vector3(ml, ap, dv);
            Vector3 dtp = new Vector3(depth, theta, phi);

            if (!trajectoryData.ContainsKey(eid))
            {
                trajectoryData[eid] = new Dictionary<int, Vector3[]>();
            }
            trajectoryData[eid].Add(probe, new Vector3[]{ mlapdv, dtp});
        }
    }

    private void ParseTaskInfo(UnityEngine.Object obj)
    {
       
        //else if (obj.name.Contains("uuid_idx"))
        //{
        //    // This file contains the UUID indexes for these clusters
        //    // save this into sessionUUIDs
        //    string data = uriTargets[2];
        //    string[] clusterIdxs = data.Split(',');
        //    if (!sessionCluUUIDIdxs.ContainsKey(eid))
        //    {
        //        sessionCluUUIDIdxs.Add(eid, new Dictionary<int, int[]>());
        //    }
        //    // Note -1 because the last thing in the file is a comma
        //    sessionCluUUIDIdxs[eid].Add(probeNum, new int[clusterIdxs.Length - 1]);
        //    for (int i = 0; i < (clusterIdxs.Length-1); i++)
        //    {
        //        string cIdx = clusterIdxs[i];
        //        sessionCluUUIDIdxs[eid][probeNum][i] = int.Parse(cIdx);
        //    }
        //}
    }


    //private string[] dataTypes = { "spikes.times", "spikes.clusters", "wheel.position", "wheel.timestamps", "goCue_times", "feedback_times", "feedbackType" };

    private void SetupTask()
    {
        // reset indexes
        taskTime = 0f;
        wi = 0;
        gi = 0;
        fi = 0;
        li = 0;
        videoPlaying = false;

        probes = new List<int>();

        // Get the key in the trajectoryData file
        // note there's an issue here, the cEID seems to have an extra character in it? Maybe a \n or something
        // [TODO: look into fixing]
        string trajKey = "";
        foreach (string key in trajectoryData.Keys)
        {
            if (cEID.Contains(key))
            {
                trajKey = key;
                break;
            }
        }
        if (trajKey.Length == 0)
        {
            Debug.LogError("eid: " + cEID + " is missing in trajectory data keys");
        }

        foreach (int probe in sessionCluUUIDIdxs[cEID].Keys)
        {
            probes.Add(probe);
            // Make the actual probes visible
            AddVisualProbe(trajKey, probe);
        }
        si = new int[probes.Count];
        Debug.Log("Found " + probes.Count + " probes in this EID");

        foreach (VideoPlayer video in videos)
        {
            video.Prepare();
        }
    }

    private void AddVisualProbe(string eid, int pid)
    {
        Vector3 mlapdv = trajectoryData[eid][pid][0] / scale;
        tips[pid].localPosition = new Vector3(-mlapdv.x, -mlapdv.z, mlapdv.y);
        // angle of attack
        Vector3 angles = trajectoryData[eid][pid][1];
        tips[pid].localRotation = Quaternion.Euler(new Vector3(0f,angles.z,angles.y));
        // depth
        tips[pid].Translate(Vector3.down * angles.x / scale);
        tips[pid].gameObject.SetActive(true);
    }

    private void ClearVisualProbes()
    {
        foreach (Transform t in tips)
        {
            t.gameObject.SetActive(false);
        }
    }

    public void LoadNeurons()
    {
        // Get the MLAPDV data 

        neurons = new Dictionary<int, Entity>();
        List<float3> positions = new List<float3>();
        List<Color> replayComp = new List<Color>();

        Color[] colors = { new Color(0.42f, 0.93f, 1f, 0.4f), new Color(1f, 0.78f, 0.32f, 0.4f) };

        foreach (int probe in probes)
        {
            Debug.Log("Initializing components and positions for neurons in probe: " + probe);
            for (int i = 0; i < sessionCluUUIDIdxs[cEID][probe].Length; i++)
            {
                int uuidIdx = sessionCluUUIDIdxs[cEID][probe][i];
                float ml = (float)mlapdv[uuidIdx]["ml"] / scale;
                float dv = (float)mlapdv[uuidIdx]["dv"] / scale;
                float ap = (float)mlapdv[uuidIdx]["ap"] / scale;
                positions.Add(new float3(ml, ap, dv));
                replayComp.Add(colors[probe]);
            }

        }

        List<Entity> neuronEntities = nemanager.AddNeurons(positions, replayComp);

        foreach (int probe in probes)
        {
            int offset = probe == 0 ? 0 : sessionCluUUIDIdxs[cEID][0].Length;
            Debug.Log("Saving entities for probe: " + probe);
            for (int i = 0; i < sessionCluUUIDIdxs[cEID][probe].Length; i++)
            {
                int uuidIdx = sessionCluUUIDIdxs[cEID][probe][i];
                neurons.Add(uuidIdx, neuronEntities[offset+i]);
            }
        }


        dataLoaded = true;
    }

    public override float TaskTime()
    {
        return taskTime;
    }

    public override void RunTask()
    {
        SetTaskRunning(true);
    }
    public override void PauseTask()
    {
        Debug.LogWarning("Pause not implemented, currently stops task");
        StopTask();
    }

    public override void StopTask()
    {
        ClearVisualProbes();
        SetTaskRunning(false);
    }

    public override void TaskUpdate()
    {
        if (TaskLoaded() && TaskRunning())
        {

            if (!dataLoaded)
            {
                SetupTask();
                LoadNeurons();
            }
            else
            {
                // If the video is not playing yet
                if (!videoPlaying)
                {
                    if (taskTime >= 8.6326566f)
                    {
                        Debug.Log("Starting videos");
                        foreach (VideoPlayer video in videos)
                        {
                            video.Play();
                        }
                        videoPlaying = true;
                    }
                }

                // Play the current spikes
                taskTime += Time.deltaTime;
                UpdateTime();

                int spikesThisFrame = 0;
                foreach (int probe in probes)
                {
                    spikesThisFrame += PlaySpikes(probe);
                }

                // TODO: setting a max of 100 is bad for areas that have high spike rates
                // also this creates sound issues if your framerate is low
                if (UnityEngine.Random.value < (spikesThisFrame / 100))
                {
                    Debug.LogWarning("Spiking but emanager has no queue for spikes anymore");
                    //emanager.QueueSpike();
                }

                // Increment the wheel index if time has passed the previous value
                while (taskTime >= wheelTime.y)
                {
                    wi++;
                    wheelTime = new Vector2((float)(double)sessionData[cEID]["wheel.timestamps"].GetValue(wi), (float)(double)sessionData[cEID]["wheel.timestamps"].GetValue(wi + 1));
                    wheelPos = new Vector2((float)(double)sessionData[cEID]["wheel.position"].GetValue(wi), (float)(double)sessionData[cEID]["wheel.position"].GetValue(wi + 1));
                    float dwheel = (wheelPos.y - wheelPos.x) * -rad2mm2deg;
                    stimPosDeg += new Vector2(dwheel, dwheel);

                    // Move stimuli
                    // Freeze stimuli if they go past zero, or off the screen
                    if (stimL!=null && !stimFrozen)
                    {
                        vsmanager.SetStimPositionDegrees(stimL, new Vector2(stimPosDeg.x, 0));
                        if (stimPosDeg.x > stimAzMinMax.x || stimPosDeg.x < -stimAzMinMax.y) { stimFrozen = true; }
                    }
                    if (stimR != null && !stimFrozen)
                    {
                        vsmanager.SetStimPositionDegrees(stimR, new Vector2(stimPosDeg.y, 0));
                        if (stimPosDeg.y < stimAzMinMax.x || stimPosDeg.y > stimAzMinMax.y) { stimFrozen = true; }
                    }
                }
                float partialTime = (taskTime - wheelTime.x) / (wheelTime.y - wheelTime.x);
                // note the negative, because for some reason the rotations are counter-clockwise
                wheelTransform.localRotation = Quaternion.Euler(-rad2deg * Mathf.Lerp(wheelPos.x, wheelPos.y, partialTime), 0, 0);

                // Check if go cue time was passed
                if (taskTime >= (double)sessionData[cEID]["goCue_times"].GetValue(gi))
                {
                    audmanager.PlayGoTone();
                    // Stimulus shown

                    // Check left or right contrast
                    float conL = (float)(double)sessionData[cEID]["contrastLeft"].GetValue(gi);
                    float conR = (float)(double)sessionData[cEID]["contrastRight"].GetValue(gi);
                    stimPosDeg = new Vector2(-1 * stimAz, stimAz);

                    stimFrozen = false;
                    // We'll do generic stimulus checks, even though the task is detection so that
                    // if later someone does 2-AFC we are ready
                    if (conL > 0)
                    {
                        Debug.Log("Adding left stimulus");
                        stimL = vsmanager.AddNewStimulus("gabor");
                        stimL.GetComponent<VisualStimulus>().SetScale(5);
                        // Set the position properly
                        vsmanager.SetStimPositionDegrees(stimL, new Vector2(stimPosDeg.x, 0));
                        vsmanager.SetContrast(stimL, conL);
                    }

                    if (conR > 0)
                    {
                        Debug.Log("Adding right stimulus");
                        stimR = vsmanager.AddNewStimulus("gabor");
                        stimR.GetComponent<VisualStimulus>().SetScale(5);
                        // Set the position properly
                        vsmanager.SetStimPositionDegrees(stimR, new Vector2(stimPosDeg.y, 0));
                        vsmanager.SetContrast(stimR, conR);
                    }
                    gi++;
                }

                // Check if feedback time was passed
                if (taskTime >= (double)sessionData[cEID]["feedback_times"].GetValue(fi))
                {
                    // Check type of feedback
                    if ((long)sessionData[cEID]["feedbackType"].GetValue(fi) == 1)
                    {
                        // Reward + lick
                        lickBehavior.Drop();
                    }
                    else
                    {
                        // Play white noise
                        audmanager.PlayWhiteNoise();
                    }
                    stimFrozen = true;

                    if (stimL != null) { vsmanager.DelayedDestroy(stimL, 1); }
                    if (stimR != null) { vsmanager.DelayedDestroy(stimR, 1); }
                    fi++;
                }

                // Check if lick time was passed
                if (taskTime >= (double)sessionData[cEID]["lick.times"].GetValue(li))
                {
                    lickBehavior.Lick();

                    li++;
                }
            }
        }
    }

    private int PlaySpikes(int probe)
    {
        int spikesThisFrame = 0;
        string ststr = "";
        string scstr = "";
        if (probe==0)
        {
            ststr = "spikes.times0";
            scstr = "spikes.clusters0";
        } else if (probe==1)
        {
            ststr = "spikes.times1";
            scstr = "spikes.clusters1";
        } else
        {
            Debug.LogError("Probe *should* not exist!! Got " + probe + " expected value 0/1");
        }
        while (taskTime >= (double)sessionData[cEID][ststr].GetValue(si[probe]))
        {
            int clu = (int)(uint)sessionData[cEID][scstr].GetValue(si[probe]);
            int uuid = sessionCluUUIDIdxs[cEID][probe][clu];

            nemanager.SetComponentData(neurons[uuid], spikedComponent);
            spikesThisFrame++;
            si[probe]++;
        }

        return spikesThisFrame;
    }

    public override void LoadTask()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Handle a change in the Time.timeScale value
    /// </summary>
    public override void ChangeTimescale()
    {
        replayManager.UpdateVideoSpeed();
    }

    public override void SetTaskTime(float newTime)
    {
        throw new NotImplementedException();
    }
}
