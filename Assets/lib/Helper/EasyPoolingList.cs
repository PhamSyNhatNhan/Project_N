using System.Collections.Generic;
using UnityEngine;

public class EasyPoolingList
{
    private List<GameObject> list = new List<GameObject>();
    private GameObject prefab;
    
    public void SetPrefab(GameObject prefab)
    {
        this.prefab = prefab;
    }

    /// <summary>
    /// Retrieves an available object (disable object) from the pool. If none are available, creates a new one.
    /// Lấy một object có sẵn (object đang disable) trong pool. Nếu không có object nào sẵn sàng, tạo mới một object.
    /// </summary>
    public GameObject GetGameObject()
    {
        foreach (GameObject obj in list)
        {
            if (obj != null && obj.activeSelf == false)
                return obj;
        }
    
        if (prefab != null)
        {
            GameObject newObj = Object.Instantiate(prefab);
            newObj.SetActive(false);
            list.Add(newObj);
            return newObj;
        }

        Debug.LogWarning("No prefab set! / Chưa gán Prefab!");
        return null;
    }

    /// <summary>
    /// Deactivates all objects in the pool.
    /// Tắt tất cả object trong pool.
    /// </summary>
    public void ReturnAllToPool()
    {
        foreach (var obj in list)
        {
            if (obj != null)
            {
                obj.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Returns a specific object to the pool.
    /// Trả một object cụ thể về pool.
    /// </summary>
    public void ReturnToPool(GameObject obj)
    {
        if (obj != null)
        {
            obj.SetActive(false);
        }
    }

    /// <summary>
    /// Destroys all objects and clears the list.
    /// Hủy tất cả object và làm sạch danh sách.
    /// </summary>
    public void ClearPool()
    {
        foreach (GameObject obj in list)
        {
            if (obj != null)
            {
                Object.Destroy(obj);
            }
        }
        list.Clear();
    }
}