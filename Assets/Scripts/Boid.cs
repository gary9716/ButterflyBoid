﻿using UnityEngine;
using System.Collections;
using System;

public class Boid : MonoBehaviour
{
  [Serializable]
  public class Settings
  {
    public float SpeedMultipliyer = 3.0f;
    public float ViewRadius = 0.5f;
    public float OptDistance = 0.1f;
    public float MinSpeed { get{ return 0.1f * SpeedMultipliyer; } }
    public float InclineFactor { get{ return 300.0f / SpeedMultipliyer; } }
    public float AligmentForcePart = 0.002f;
    public float TotalForceMultipliyer = 12;
    public float Inertness = 0.5f;
    public float VerticalPriority = 1.0f;

    [System.Xml.Serialization.XmlIgnore]
    public Trace Trace { get; set; }
    public float AttractrionForce = 0.02f;
  }

  [Serializable]
  public class DebugSettings
  {
    public bool enableDrawing = false;

    public bool obstaclesAvoidanceDraw = false;
    public Color obstaclesAvoidanceColor = Color.red;

    public bool velocityDraw = false;
    public Color velocityColor = Color.grey;

    public bool positionForceDraw = false;
    public Color positionForceColor = Color.cyan;

    public bool alignmentForceDraw = false;
    public Color alignmentForceColor = Color.yellow;

    public bool cohesionForceDraw = false;
    public Color cohesionForceColor = Color.magenta;

    public bool collisionsAvoidanceForceDraw = false;
    public Color collisionsAvoidanceForceColor = Color.green;

    public bool attractionForceDraw = false;
    public Color attractionForceColor = Color.green;

    public bool totalForceDraw = false;
    public Color totalForceColor = Color.black;
  }

  public interface ITrigger
  {
    void OnTouch( Boid boid );
  }

  private Settings sts = null;
  public Settings SettingsRef {
    get { return sts; }
    set { sts = value; }
  }

  private DebugSettings dbgSts = null;
  public DebugSettings DebugSettingsRef {
    get { return dbgSts; }
    set { dbgSts = value; }
  }

  private Vector3 velocity = Vector3.zero;
  public Vector3 Velocity { get{ return velocity; } }

  void Start()
  {
    if( sts == null )
    {
      sts = Main.GetSettings( gameObject );

      if( sts != null )
        Debug.LogWarning( "Boid initialized with default settings" );
      else
      {
        Debug.LogWarning( "Boid initialized with standalone settings copy" );
        sts = new Settings();
      }
    }

    if( dbgSts == null )
    {
      Debug.LogWarning( "Boid initialized with standalone debug settings copy" );
      dbgSts = new DebugSettings();
    }
  }

  BoidTools.SeparationForce sepForce = new BoidTools.SeparationForce();
  BoidTools.CollisionAvoidanceForce collAvoid = new BoidTools.CollisionAvoidanceForce();
  void FixedUpdate()
  {
    //Algorithm based on
    //http://www.cs.toronto.edu/~dt/siggraph97-course/cwr87/
    //http://www.red3d.com/cwr/boids/

    //Bird is affected by 3 base forses:
    // cohesion
    // separation + collisionAvoidance
    // alignmentForce

    sepForce.changeSetting(sts);
    collAvoid.changeSetting( sts, sepForce.Calc(sts.OptDistance) );

    //Geometric center of visible birds
    var centeroid = Vector3.zero;

    var collisionAvoidance = Vector3.zero;
    var avgSpeed = Vector3.zero;
    var neighbourCount = 0;

    //Store it as an optimization
    var direction = transform.rotation * Vector3.forward;
    var curPos = transform.position;
   
    foreach( var vis in Physics.OverlapSphere(curPos, sts.ViewRadius) )
    {
      var visPos = vis.transform.position;
      Boid boid;
      ITrigger trigger;

      if( (boid = vis.GetComponent<Boid>()) != null ) //Birds processing
      {
        Vector3 separationForce;

        if( !sepForce.Calc(curPos, visPos, out separationForce) )
          continue;

        collisionAvoidance += separationForce;
        ++neighbourCount;
        centeroid += visPos;
        avgSpeed += boid.velocity;
      }
      else if( (trigger = vis.GetInterface<ITrigger>()) != null )
      {
        if( GetComponent<Collider>().bounds.Intersects(vis.bounds) )
          trigger.OnTouch(this);
      }
      else //Obstacles processing
      {
        BoidTools.CollisionAvoidanceForce.Force force;
        if( collAvoid.Calc(curPos, direction, vis, out force) )
        {
          collisionAvoidance += force.dir;

          if( dbgSts.enableDrawing && dbgSts.obstaclesAvoidanceDraw )
            Drawer.DrawRay( force.pos, force.dir, dbgSts.obstaclesAvoidanceColor );
        }
      }
    }

    if( neighbourCount > 0 )
    {
      //Cohesion force. It makes united formula with BoidTools.SeparationForce
      centeroid = centeroid / neighbourCount - curPos;

      //Spherical shape of flock looks unnatural, so let's scale it along y axis
      centeroid.y *= sts.VerticalPriority;

      //Difference between current bird speed and average speed of visible birds
      avgSpeed = avgSpeed / neighbourCount - velocity;
    }

    var positionForce = (1.0f - sts.AligmentForcePart) * sts.SpeedMultipliyer * (centeroid + collisionAvoidance);
    var alignmentForce = sts.AligmentForcePart * avgSpeed / Time.deltaTime;
    var attractionForce = CalculateAttractionForce( sts, curPos, velocity );
    var totalForce = sts.TotalForceMultipliyer * ( positionForce + alignmentForce + attractionForce );

    var newVelocity = (1 - sts.Inertness) * (totalForce * Time.deltaTime) + sts.Inertness * velocity;

    velocity = CalcNewVelocity( sts.MinSpeed, velocity, newVelocity, direction );

    var rotation = CalcRotation( sts.InclineFactor, velocity, totalForce );

    if( MathTools.IsValid(rotation) )
      gameObject.transform.rotation = rotation;

    /////////////////////////////////////////////////////////////
    // Debug drawing
    /////////////////////////////////////////////////////////////
    /* 
    if( dbgSts.enableDrawing )
    {
      if( dbgSts.velocityDraw )
        Drawer.DrawRay( curPos, velocity, dbgSts.velocityColor );
  
      if( dbgSts.positionForceDraw )
        Drawer.DrawRay( curPos, positionForce, dbgSts.positionForceColor );
  
      if( dbgSts.alignmentForceDraw )
        Drawer.DrawRay( curPos, alignmentForce, dbgSts.alignmentForceColor );

      if( dbgSts.cohesionForceDraw )
        Drawer.DrawRay( curPos, centeroid, dbgSts.cohesionForceColor );
  
      if( dbgSts.collisionsAvoidanceForceDraw )
        Drawer.DrawRay( curPos, collisionAvoidance, dbgSts.collisionsAvoidanceForceColor );

      if( dbgSts.attractionForceDraw )
        Drawer.DrawRay( curPos, attractionForce, dbgSts.attractionForceColor );

      if( dbgSts.totalForceDraw )
        Drawer.DrawRay( curPos, totalForce, dbgSts.totalForceColor );
    }
    */
  }

  void Update()
  {
    transform.position += velocity * Time.deltaTime;
  }


  //Force which attracts birds to waypoints
  static Vector3 CalculateAttractionForce( Settings sts, Vector3 curPos, Vector3 curVelocity )
  {
    if( !sts.Trace )
      return Vector3.zero;

    var attrPos = sts.Trace.GetAttractionPoint();
    var direction = (attrPos - curPos).normalized;

    //The force have an effect only on direction and shouldn't increase speed if bird flies in the right direction
    var factor = sts.AttractrionForce * sts.SpeedMultipliyer * MathTools.AngleToFactor( direction, curVelocity );

    return factor * direction;
  }

  static Vector3 CalcNewVelocity( float minSpeed, Vector3 curVel, Vector3 dsrVel, Vector3 defaultVelocity )
  {
    //We have to take into account that bird can't change their direction instantly. That's why
    //dsrVel (desired velocity) influence first of all on flying direction and after that on
    //velocity magnitude oneself

    var curVelLen = curVel.magnitude;

    if( curVelLen > MathTools.epsilon )
      curVel /= curVelLen;
    else
    {
      curVel = defaultVelocity;
      curVelLen = 1;
    }

    var dsrVelLen = dsrVel.magnitude;
    var resultLen = minSpeed;

    if( dsrVelLen > MathTools.epsilon )
    {
      dsrVel /= dsrVelLen;

      //We spend a part of velocity magnitude on bird rotation and the rest of it on speed magnitude changing

      //Map rotation to factor [0..1]
      var angleFactor = MathTools.AngleToFactor(dsrVel, curVel);

      //If dsrVel magnitude is twice bigger than curVelLen then bird can rotate on any angle
      var rotReqLength = 2 * curVelLen * angleFactor;

      //Velocity magnitude remained after rotation
      var speedRest = dsrVelLen - rotReqLength;

      if( speedRest > 0 )
      {
        curVel = dsrVel;
        resultLen = speedRest;
      }
      else
      {
        curVel = Vector3.Slerp( curVel, dsrVel, dsrVelLen / rotReqLength );
      }

      if( resultLen < minSpeed )
        resultLen = minSpeed;
    }

    return curVel * resultLen;
  }

  static Quaternion NaNQ = new Quaternion( float.NaN, float.NaN, float.NaN, float.NaN );

  //Birds should incline when they turn
  static Quaternion CalcRotation( float inclineFactor, Vector3 velocity, Vector3 totalForce )
  {
    if( velocity.sqrMagnitude < MathTools.sqrEpsilon )
      return NaNQ;

    //We project force on right vector and multiply it by factor

    //Instead of true calculation of right vector we use a trick with projection on XZ, but
    //this trick doesn't work if bird flies strictly vertically. In order to fix it we
    //have to know unmodified UP vector of bird.
    var rightVec = MathTools.RightVectorXZProjected(velocity);
    var inclineDeg = MathTools.VecProjectedLength( totalForce, rightVec ) * -inclineFactor;
    return Quaternion.LookRotation( velocity ) * Quaternion.AngleAxis(Mathf.Clamp(inclineDeg, -90, 90), Vector3.forward);
  }
}
