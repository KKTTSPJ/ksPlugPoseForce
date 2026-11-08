using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace kPlugPoseForceExport
{
    // 書き出しが変更する欄と、確認用の骨・IK の状態を保持する。
    internal sealed class PoseForceExportState
    {
        private sealed class Slot
        {
            internal object Owner, Value;
            internal FieldInfo Field;
            internal PropertyInfo Property;
            internal object Read() { return Field != null ? Field.GetValue(Owner) : Property.GetValue(Owner, null); }
            internal void Write(object value)
            {
                if (Field != null) Field.SetValue(Owner, value);
                else Property.GetSetMethod(true).Invoke(Owner, new[] { value });
            }
        }

        private sealed class Bone
        {
            internal Transform Transform;
            internal Vector3 Position, Scale;
            internal Quaternion Rotation;
        }

        private readonly List<Slot> _slots = new List<Slot>();
        private readonly List<Bone> _bones = new List<Bone>();
        private static readonly Dictionary<Type, Dictionary<string, MemberInfo>> Members = new Dictionary<Type, Dictionary<string, MemberInfo>>();

        private static MemberInfo Member(Type type, string name)
        {
            Dictionary<string, MemberInfo> byName;
            if (!Members.TryGetValue(type, out byName))
            {
                byName = new Dictionary<string, MemberInfo>();
                Members.Add(type, byName);
            }
            MemberInfo member;
            if (byName.TryGetValue(name, out member)) return member;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            // フィールドとプロパティの両方を、警告を出さずに調べる。見つかったメンバーだけを再利用する。
            for (var current = type; current != null; current = current.BaseType)
            {
                member = (MemberInfo)current.GetField(name, flags) ?? current.GetProperty(name, flags);
                if (member == null) continue;
                byName.Add(name, member);
                return member;
            }
            throw new MissingMemberException(type.FullName, name);
        }

        internal static FieldInfo Field(Type type, string name)
        {
            var field = Member(type, name) as FieldInfo;
            if (field == null) throw new MissingFieldException(type.FullName, name);
            return field;
        }

        internal static object Get(object owner, string name)
        {
            var type = owner as Type ?? owner.GetType();
            var member = Member(type, name);
            var field = member as FieldInfo;
            if (field != null) return field.GetValue(owner is Type ? null : owner);
            var prop = member as PropertyInfo;
            if (prop == null || prop.GetGetMethod(true) == null) throw new MissingMemberException(type.FullName, name);
            return prop.GetValue(owner is Type ? null : owner, null);
        }

        internal static void Set(object owner, string name, object value)
        {
            var type = owner as Type ?? owner.GetType();
            var member = Member(type, name);
            var field = member as FieldInfo;
            if (field != null) { field.SetValue(owner is Type ? null : owner, value); return; }
            var prop = member as PropertyInfo;
            if (prop == null || prop.GetSetMethod(true) == null) throw new MissingMemberException(type.FullName, name);
            prop.GetSetMethod(true).Invoke(owner is Type ? null : owner, new[] { value });
        }

        internal void Save(object owner, string name)
        {
            var type = owner as Type ?? owner.GetType();
            var member = Member(type, name);
            var slot = new Slot { Owner = owner is Type ? null : owner, Field = member as FieldInfo };
            if (slot.Field == null)
            {
                slot.Property = member as PropertyInfo;
                if (slot.Property == null || slot.Property.GetSetMethod(true) == null)
                    throw new MissingMemberException(type.FullName, name);
            }
            slot.Value = slot.Read();
            _slots.Add(slot);
        }

        internal void SaveCharacter(ChaControl cha, object ik)
        {
            if (cha == null || ik == null) throw new InvalidOperationException("キャラと IK の初期化を待ってください。");
            foreach (var t in cha.GetComponentsInChildren<Transform>(true))
                _bones.Add(new Bone { Transform = t, Position = t.localPosition, Rotation = t.localRotation, Scale = t.localScale });
            var solver = Get(ik, "solver");
            foreach (string name in new[] { "leftHandEffector", "rightHandEffector", "leftFootEffector", "rightFootEffector" })
            {
                var eff = Get(solver, name);
                Save(eff, "target");
                Save(eff, "positionWeight");
                Save(eff, "rotationWeight");
            }
        }

        internal int BoneCount { get { return _bones.Count; } }

        // 例外が出ても、残りの欄の復元を続ける。
        internal void RestoreAndVerify()
        {
            var errors = new List<string>();
            for (int i = _slots.Count - 1; i >= 0; --i)
            {
                var slot = _slots[i];
                try { slot.Write(slot.Value); }
                catch (Exception ex) { errors.Add(ex.Message); }
            }
            int changedBones = 0;
            foreach (var b in _bones)
            {
                try
                {
                    if (b.Transform == null) { errors.Add("骨が失われました"); continue; }
                    if (!b.Position.Equals(b.Transform.localPosition) || !b.Rotation.Equals(b.Transform.localRotation)
                        || !b.Scale.Equals(b.Transform.localScale))
                    {
                        ++changedBones;
                        b.Transform.localPosition = b.Position;
                        b.Transform.localRotation = b.Rotation;
                        b.Transform.localScale = b.Scale;
                    }
                }
                catch (Exception ex) { errors.Add(ex.Message); }
            }
            foreach (var slot in _slots)
            {
                try { if (!Equals(slot.Value, slot.Read())) errors.Add("欄の復元後の値が一致しません"); }
                catch (Exception ex) { errors.Add(ex.Message); }
            }
            PoseForceExportPlugin.Logger.LogInfo("[PoseForceExport] 復元検証: 骨=" + BoneCount + ", 採取中の骨の変化="
                + changedBones + ", 復元エラー=" + errors.Count);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("; ", errors.ToArray()));
            if (changedBones != 0) throw new InvalidOperationException("採取中に骨の値が変化したため復元しました。書き出しは中止します。");
        }
    }
}
